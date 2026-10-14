using System.Collections.Concurrent;
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
    /// Finds the played games among every EA game (~10 requests of
    /// <see cref="EaClient.RecentGamesBatchSize"/> slugs, <see cref="EaClient.RequestsAtOnce"/>
    /// at a time), names them, reads the achievements of the games played since
    /// the last refresh, and replaces the cache. Throws <see cref="EaException"/>
    /// (token expired...): the old cache is kept.
    /// </summary>
    /// <param name="progress">Receives (done, total) while the played games are looked for, in order.</param>
    public async Task<GameHistory> RefreshAsync(EaClient client, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var slugs = await client.FetchGameSlugsAsync(ct).ConfigureAwait(false);
        var batches = slugs.Chunk(EaClient.RecentGamesBatchSize).ToList();
        // A few at a time (EaClient.RequestsAtOnce), each answer at its batch's
        // place: the cache is the same as one by one. Each answer is parsed BEFORE
        // saving, as it arrives: an unexpected one, or any error, stops the sync and
        // the previous, valid cache is kept (an incomplete history is never saved).
        var answers = new (string Json, IReadOnlyList<EaPlayedGame> Found)[batches.Count];
        int done = 0;
        var progressLock = new object();
        await Parallel.ForEachAsync(Enumerable.Range(0, batches.Count),
            new ParallelOptions { MaxDegreeOfParallelism = EaClient.RequestsAtOnce, CancellationToken = ct },
            async (i, token) =>
            {
                string json = await client.FetchRecentGamesJsonAsync(batches[i], token).ConfigureAwait(false);
                var found = EaClient.ParseRecentGames(json);
                // A full page may have been cut (EA gives no total): never save an incomplete history.
                if (found.Count >= EaClient.RecentGamesBatchSize)
                    throw new EaException(CoreStrings.Ea_Format);
                answers[i] = (json, found);
                // Counted and reported together: the progress never goes back.
                lock (progressLock)
                    progress?.Report((++done, batches.Count));
            }).ConfigureAwait(false);

        // Only the answers with games are kept.
        var recentAnswers = answers.Where(a => a.Found.Count > 0).Select(a => a.Json).ToList();
        var played = answers.SelectMany(a => a.Found).ToList();

        var gamesAnswers = new List<string>();
        var products = new List<EaGameProduct>();
        foreach (var batch in played.Select(g => g.Slug).Distinct().Chunk(EaClient.GamesBatchSize))
        {
            string json = await client.FetchGamesJsonAsync(batch, ct).ConfigureAwait(false);
            products.AddRange(EaClient.ParseGames(json));
            gamesAnswers.Add(json);
        }

        var known = LoadAchievements();
        await FetchAchievementsAsync(client, played, products, known, ct).ConfigureAwait(false);

        await _cache.SaveAsync(HistoryKey,
            $$"""{"recentGames":{{CacheStore.JoinRaw(recentAnswers)}},"games":{{CacheStore.JoinRaw(gamesAnswers)}}}""", ct).ConfigureAwait(false);
        return new GameHistory(ToPlayedGames(played, products, known), DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The achievements of the games played since their entry was saved, one
    /// request each, several at a time. One game failing (network, odd answer) is asked again next
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

        // Several at a time, as many as recentGames (EaClient.RequestsAtOnce; not measured for achievements).
        var fetched = new ConcurrentDictionary<string, AchievementEntry>();
        try
        {
            string playerId = await client.FetchPlayerIdAsync(ct).ConfigureAwait(false);
            await Parallel.ForEachAsync(toFetch,
                new ParallelOptions { MaxDegreeOfParallelism = EaClient.RequestsAtOnce, CancellationToken = ct },
                async (game, token) =>
                {
                    try
                    {
                        var achievements = await client.FetchAchievementsAsync(offers[game.Slug], playerId, token).ConfigureAwait(false);
                        fetched[game.Slug] = new AchievementEntry(achievements?.Unlocked ?? 0, achievements?.Total ?? 0, game.LastPlayed);
                    }
                    // Skipped, asked again next time. Against ct, not token: the requests a refused
                    // one stops are skipped too, so only that refusal goes up.
                    catch (Exception ex) when (StoreLookup.Transient(ex, ct) || ex is EaException { IsTokenRefused: false, IsRateLimited: false })
                    {
                    }
                }).ConfigureAwait(false);
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
                var progress = AchievementProgress.From(entry?.Unlocked ?? 0, entry?.Total ?? 0);

                return new PlayedGame(
                    Platform: EaClient.PlatformName,
                    PlatformId: game.Slug,
                    // No product found: the slug, readable ("battlefield-4" → "Battlefield 4").
                    Name: names.GetValueOrDefault(game.Slug)
                        ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(game.Slug.Replace('-', ' ')),
                    HasAchievements: progress.HasAchievements,
                    UnlockedAchievements: progress.Unlocked,
                    TotalAchievements: progress.Total,
                    ProgressPercentage: progress.Percentage,
                    LastPlayed: game.LastPlayed,
                    Status: progress.Status,
                    PlaytimeMinutes: game.PlaytimeMinutes);
            })
            .OrderByDescending(g => g.LastPlayed)
            .ToList();
    }

    private Dictionary<string, AchievementEntry> LoadAchievements() =>
        _cache.LoadDictionary(AchievementsKey, EaJsonContext.Default.DictionaryStringAchievementEntry);

}
