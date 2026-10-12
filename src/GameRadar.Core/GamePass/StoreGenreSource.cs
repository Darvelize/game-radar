using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.GamePass;

/// <summary>
/// Finds the genres of the played games: from the Game Pass catalog when
/// the game is in it, otherwise from the Microsoft Store (lookup by Xbox
/// title id). Store results are cached for good: a game's genres never change.
/// </summary>
public sealed class StoreGenreSource
{
    // A failed lookup is retried after this delay only (the Store may add
    // the game later); meanwhile it costs no request at each launch.
    private static readonly TimeSpan RetryNotFoundAfter = TimeSpan.FromDays(30);

    // Pause between two lookups: gentle with an unofficial endpoint.
    private static readonly TimeSpan DelayBetweenLookups = TimeSpan.FromMilliseconds(100);

    private readonly CacheStore _cache;
    private readonly string _cacheKey;

    /// <summary>The key of the cache this source reads (see <see cref="CacheCleanup"/>).</summary>
    internal string CacheKey => _cacheKey;

    public StoreGenreSource(CacheStore cache, string market = "FR")
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        // v2: entries also hold play modes; v1 entries (genres only) are
        // ignored and looked up again once.
        _cacheKey = $"store-genres-{market.ToLowerInvariant()}-v2";
    }

    /// <summary>
    /// Returns the played games that have achievements, with their genres.
    /// Only games missing from the local cache cost network requests
    /// (1 lookup each, then 1 request per 200 products).
    /// </summary>
    /// <param name="progress">Receives (done, total) during the lookups.</param>
    public async Task<IReadOnlyList<PlayedGameWithGenres>> ResolveAsync(
        IEnumerable<PlayedGame> history,
        CatalogMatcher matcher,
        GamePassClient client,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var known = LoadEntries();

        // Games without achievements (mostly PC games from other stores)
        // give no engagement signal: no need to know their genres.
        var games = history.Where(g => g.HasAchievements).ToList();

        var toLookUp = games
            .Where(g => g.Platform == XboxLiveClient.PlatformName && matcher.Find(g) is null)
            .Select(g => g.PlatformId)
            .Distinct()
            .Where(id => !known.TryGetValue(id, out var entry)
                || (entry.Genres.Count == 0 && now - entry.CheckedAt > RetryNotFoundAfter))
            .ToList();

        // The Store down, too slow or answering oddly: the games not reached
        // stay unknown, looked up next time.
        if (toLookUp.Count > 0)
            await StoreLookup.RunAsync(_cache, _cacheKey, known,
                found => LookUpAsync(toLookUp, found, client, progress, now, ct),
                GamePassJsonContext.Default.DictionaryStringGenreEntry, ct).ConfigureAwait(false);

        var result = new List<PlayedGameWithGenres>();
        foreach (var game in games)
        {
            if (matcher.Find(game) is { } match)
                result.Add(new PlayedGameWithGenres(game, match.Genres, match.Modes));
            else if (known.TryGetValue(game.PlatformId, out var entry) && entry.Genres.Count > 0)
                result.Add(new PlayedGameWithGenres(game, entry.Genres, entry.Modes));
        }
        return result;
    }

    private static async Task LookUpAsync(
        List<string> titleIds,
        Dictionary<string, GenreEntry> found,
        GamePassClient client,
        IProgress<(int Done, int Total)>? progress,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Step 1: title id -> product id, one request per game.
        var productIds = new Dictionary<string, string>();
        for (int i = 0; i < titleIds.Count; i++)
        {
            progress?.Report((i, titleIds.Count));
            if (i > 0)
                await Task.Delay(DelayBetweenLookups, ct).ConfigureAwait(false);

            if (await client.LookupProductIdAsync(titleIds[i], ct).ConfigureAwait(false) is { } productId)
                productIds[titleIds[i]] = productId;
            else
                found[titleIds[i]] = new GenreEntry([], PlayModes.None, now); // remembered as "not found"
        }
        progress?.Report((titleIds.Count, titleIds.Count));

        // Step 2: product id -> genres and modes, 200 products per request.
        var allFeatures = await client.FetchFeaturesAsync(productIds.Values, ct).ConfigureAwait(false);
        foreach (var (titleId, productId) in productIds)
        {
            var features = allFeatures.GetValueOrDefault(productId);
            found[titleId] = new GenreEntry(features?.Genres ?? [], features?.Modes ?? PlayModes.None, now);
        }
    }

    private Dictionary<string, GenreEntry> LoadEntries() => _cache.LoadDictionary(_cacheKey, GamePassJsonContext.Default.DictionaryStringGenreEntry);

    /// <summary>Cached result for one title id. Empty genres = not found in the Store.</summary>
    internal sealed record GenreEntry(IReadOnlyList<string> Genres, PlayModes Modes, DateTimeOffset CheckedAt);
}
