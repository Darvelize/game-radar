using System.Globalization;
using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.EA;

/// <summary>
/// Gives the EA history: the played games (raw answers of the EA app API,
/// cached as-is and read again at each load) plus the achievements of each
/// game (compact cache, asked again only for a game played since).
/// EA is only called when the user pastes a token and asks for it.
/// </summary>
public sealed class EaHistorySource
{
    private const string HistoryKey = "ea-history";
    private const string AchievementsKey = "ea-achievements";

    private readonly CacheStore _cache;

    public EaHistorySource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <summary>
    /// Returns the last saved history, or null when there is none.
    /// No network call. Never throws: a corrupted cache counts as missing.
    /// </summary>
    public GameHistory? LoadCached()
    {
        if (_cache.TryLoad(HistoryKey) is not { } entry)
            return null;

        try
        {
            var (played, products) = ParseHistory(entry.Content);
            return new GameHistory(ToPlayedGames(played, products, LoadAchievements()), entry.SavedAt);
        }
        catch (Exception ex) when (ex is JsonException or EaException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the played games among every EA game (5 slugs per request, ~150
    /// requests), names them, reads the achievements of the games played since
    /// the last refresh, and replaces the cache. Throws <see cref="EaException"/>
    /// (token expired...): the old cache is kept.
    /// </summary>
    /// <param name="progress">Receives (done, total) while the played games are looked for.</param>
    public async Task<GameHistory> RefreshAsync(EaClient client, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var slugs = await client.FetchGameSlugsAsync(ct).ConfigureAwait(false);
        var batches = slugs.Chunk(EaClient.RecentGamesBatchSize).ToList();
        var recentAnswers = new List<string>();
        var played = new List<EaPlayedGame>();
        for (int i = 0; i < batches.Count; i++)
        {
            string json = await client.FetchRecentGamesJsonAsync(batches[i], ct).ConfigureAwait(false);
            // Parsed BEFORE saving: an unexpected answer throws here and the
            // previous, valid cache is kept. Only the answers with games are kept.
            var found = EaClient.ParseRecentGames(json);
            if (found.Count > 0)
            {
                recentAnswers.Add(json);
                played.AddRange(found);
            }
            progress?.Report((i + 1, batches.Count));
        }

        var gamesAnswers = new List<string>();
        var products = new List<EaGameProduct>();
        foreach (var batch in played.Select(g => g.Slug).Distinct().Chunk(EaClient.RecentGamesBatchSize))
        {
            string json = await client.FetchGamesJsonAsync(batch, ct).ConfigureAwait(false);
            products.AddRange(EaClient.ParseGames(json));
            gamesAnswers.Add(json);
        }

        var known = LoadAchievements();
        await FetchAchievementsAsync(client, played, products, known, ct).ConfigureAwait(false);

        await _cache.SaveAsync(HistoryKey,
            $$"""{"recentGames":[{{string.Join(",", recentAnswers)}}],"games":[{{string.Join(",", gamesAnswers)}}]}""", ct).ConfigureAwait(false);
        return new GameHistory(ToPlayedGames(played, products, known), DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The achievements of the games played since their entry was saved, one
    /// request each. One game failing (network, odd answer) is asked again next
    /// time; an expired token or the rate limit stop everything. What was read
    /// is saved whatever happens.
    /// </summary>
    private async Task FetchAchievementsAsync(
        EaClient client, IReadOnlyList<EaPlayedGame> played, IReadOnlyList<EaGameProduct> products,
        Dictionary<string, AchievementEntry> known, CancellationToken ct)
    {
        var offers = products.GroupBy(p => p.Slug).ToDictionary(g => g.Key, g => g.First().OfferId);
        var toFetch = played
            .Where(g => offers.ContainsKey(g.Slug)
                && !(known.TryGetValue(g.Slug, out var entry) && entry.LastPlayed == g.LastPlayed))
            .ToList();
        if (toFetch.Count == 0)
            return;

        var fetched = new Dictionary<string, AchievementEntry>();
        try
        {
            string playerId = await client.FetchPlayerIdAsync(ct).ConfigureAwait(false);
            foreach (var game in toFetch)
            {
                try
                {
                    var achievements = await client.FetchAchievementsAsync(offers[game.Slug], playerId, ct).ConfigureAwait(false);
                    fetched[game.Slug] = new AchievementEntry(achievements?.Unlocked ?? 0, achievements?.Total ?? 0, game.LastPlayed);
                }
                catch (Exception ex) when (StoreLookup.Transient(ex, ct) || ex is EaException { IsTokenRefused: false, IsRateLimited: false })
                {
                }
            }
        }
        finally
        {
            foreach (var (slug, entry) in fetched)
                known[slug] = entry;
            await _cache.MergeDictionaryAsync(AchievementsKey, fetched, EaJsonContext.Default.DictionaryStringAchievementEntry).ConfigureAwait(false);
        }
    }

    /// <summary>The cached raw answers → played games and their products.</summary>
    private static (List<EaPlayedGame> Played, List<EaGameProduct> Products) ParseHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("recentGames").EnumerateArray().SelectMany(a => EaClient.ParseRecentGames(a.GetRawText())).ToList(),
            root.GetProperty("games").EnumerateArray().SelectMany(a => EaClient.ParseGames(a.GetRawText())).ToList());
    }

    /// <summary>Played games + products + achievements → games, most recently played first.</summary>
    internal static IReadOnlyList<PlayedGame> ToPlayedGames(
        IEnumerable<EaPlayedGame> played,
        IEnumerable<EaGameProduct> products,
        IReadOnlyDictionary<string, AchievementEntry> achievements)
    {
        var names = products.GroupBy(p => p.Slug).ToDictionary(g => g.Key, g => g.First().Name);
        return played
            .DistinctBy(g => g.Slug)
            .Select(game =>
            {
                var entry = achievements.GetValueOrDefault(game.Slug);
                bool hasAchievements = entry is { Total: > 0 };
                int unlocked = hasAchievements ? entry!.Unlocked : 0;
                int? total = hasAchievements ? entry!.Total : null;
                double percentage = hasAchievements ? 100.0 * unlocked / total!.Value : 0;

                return new PlayedGame(
                    Platform: EaClient.PlatformName,
                    PlatformId: game.Slug,
                    // No product found: the slug, readable ("battlefield-4" → "Battlefield 4").
                    Name: names.GetValueOrDefault(game.Slug)
                        ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(game.Slug.Replace('-', ' ')),
                    HasAchievements: hasAchievements,
                    UnlockedAchievements: unlocked,
                    TotalAchievements: total,
                    ProgressPercentage: percentage,
                    LastPlayed: game.LastPlayed,
                    Status: StatusClassifier.FromAchievements(unlocked, total, percentage),
                    PlaytimeMinutes: game.PlaytimeMinutes);
            })
            .OrderByDescending(g => g.LastPlayed)
            .ToList();
    }

    private Dictionary<string, AchievementEntry> LoadAchievements() =>
        _cache.LoadDictionary(AchievementsKey, EaJsonContext.Default.DictionaryStringAchievementEntry);

    /// <summary>Achievements of one game at a given "last played" date. Total 0 = the game has none.</summary>
    internal sealed record AchievementEntry(int Unlocked, int Total, DateTimeOffset? LastPlayed);
}
