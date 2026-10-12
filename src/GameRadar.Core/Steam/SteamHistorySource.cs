using System.Collections.Concurrent;
using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// Gives the Steam history: the library (raw answer, cached as-is) plus the
/// achievements of each played game (compact cache, one entry per game:
/// the raw answers list every achievement and would weigh megabytes).
/// Steam is only called on explicit refresh by the user.
/// </summary>
public sealed class SteamHistorySource
{
    private const string LibraryKey = "steam-library";
    private const string AchievementsKey = "steam-achievements";

    // Parallel achievement requests: fast enough (~120 games in a few
    // seconds), gentle enough with the API.
    private const int ParallelRequests = 4;

    private readonly CacheStore _cache;

    public SteamHistorySource(CacheStore cache)
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
        if (_cache.TryLoad(LibraryKey) is not { } entry)
            return null;

        try
        {
            return new GameHistory(ToPlayedGames(SteamClient.ParseLibrary(entry.Content), LoadAchievements()), entry.SavedAt);
        }
        catch (Exception ex) when (ex is JsonException or SteamException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the library (1 request), then the achievements of the games
    /// played since the last refresh only (1 request each), and replaces the cache.
    /// </summary>
    /// <param name="progress">Receives (done, total) during the achievement requests.</param>
    public async Task<GameHistory> RefreshAsync(
        SteamClient client,
        string steamId,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        string json = await client.FetchLibraryJsonAsync(steamId, ct).ConfigureAwait(false);
        // Parse BEFORE saving: a private profile or an unexpected answer
        // throws here and the previous, valid cache is kept.
        var library = SteamClient.ParseLibrary(json);

        var known = LoadAchievements();
        // A game's achievements only change when it is played: an entry saved
        // with the same "last played" date is still right.
        var toFetch = library
            .Where(g => g.HasStats && g.PlaytimeMinutes > 0
                && !(known.TryGetValue(g.AppId, out var entry) && entry.LastPlayed == g.LastPlayed))
            .ToList();

        if (toFetch.Count > 0)
        {
            // Filled from several threads at once: a thread-safe dictionary.
            var fetched = new ConcurrentDictionary<string, AchievementEntry>();
            int done = 0;
            progress?.Report((0, toFetch.Count));
            try
            {
                // Parallel.ForEachAsync runs the body for several games at a
                // time, never more than MaxDegreeOfParallelism.
                await Parallel.ForEachAsync(
                    toFetch,
                    new ParallelOptions { MaxDegreeOfParallelism = ParallelRequests, CancellationToken = ct },
                    async (game, token) =>
                    {
                        try
                        {
                            var achievements = await client.FetchAchievementsAsync(steamId, game.AppId, token).ConfigureAwait(false);
                            fetched[game.AppId] = new AchievementEntry(achievements?.Unlocked ?? 0, achievements?.Total ?? 0, game.LastPlayed);
                        }
                        // This game alone failed (server error, timeout, odd answer):
                        // the others go on, and it is asked again at the next sync.
                        // A refused key or the rate limit (SteamException) stop everything.
                        catch (Exception ex) when (StoreLookup.Transient(ex, token))
                        {
                        }
                        progress?.Report((Interlocked.Increment(ref done), toFetch.Count));
                    }).ConfigureAwait(false);
            }
            finally
            {
                // Saved even after an error (rate limit...): the answers already
                // received are not lost and will not be asked again.
                foreach (var (appId, entry) in fetched)
                    known[appId] = entry;
                await _cache.MergeDictionaryAsync(AchievementsKey, fetched, SteamJsonContext.Default.DictionaryStringAchievementEntry).ConfigureAwait(false);
            }
        }

        await _cache.SaveAsync(LibraryKey, json, ct).ConfigureAwait(false);

        // The wishlist is a bonus of the recommendations, not what the sync is
        // for: any failure leaves the last wishlist in place and the sync done.
        // ponytail: the failure is not shown; add a line on the Steam status if it ever matters.
        try
        {
            await new SteamWishlistSource(_cache).RefreshAsync(client, steamId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (StoreLookup.Failed(ex, ct))
        {
        }

        return new GameHistory(ToPlayedGames(library, known), DateTimeOffset.UtcNow);
    }

    /// <summary>Library + known achievements → games, most recently played first.</summary>
    internal static IReadOnlyList<PlayedGame> ToPlayedGames(
        IReadOnlyList<SteamLibraryGame> library,
        IReadOnlyDictionary<string, AchievementEntry> achievements)
    {
        return library
            .Select(game =>
            {
                var entry = achievements.GetValueOrDefault(game.AppId);
                bool hasAchievements = entry is { Total: > 0 };
                int unlocked = hasAchievements ? entry!.Unlocked : 0;
                int? total = hasAchievements ? entry!.Total : null;
                double percentage = hasAchievements ? 100.0 * unlocked / total!.Value : 0;

                return new PlayedGame(
                    Platform: SteamClient.PlatformName,
                    PlatformId: game.AppId,
                    Name: game.Name,
                    HasAchievements: hasAchievements,
                    UnlockedAchievements: unlocked,
                    TotalAchievements: total,
                    ProgressPercentage: percentage,
                    LastPlayed: game.LastPlayed,
                    Status: StatusClassifier.FromAchievements(unlocked, total, percentage),
                    PlaytimeMinutes: game.PlaytimeMinutes)
                {
                    SteamAppId = game.AppId
                };
            })
            .OrderByDescending(g => g.LastPlayed)
            .ToList();
    }

    private Dictionary<string, AchievementEntry> LoadAchievements() => _cache.LoadDictionary(AchievementsKey, SteamJsonContext.Default.DictionaryStringAchievementEntry);

    /// <summary>Achievements of one game at a given "last played" date. Total 0 = the game has none.</summary>
    internal sealed record AchievementEntry(int Unlocked, int Total, DateTimeOffset? LastPlayed);
}
