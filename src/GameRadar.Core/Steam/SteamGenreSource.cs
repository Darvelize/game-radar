using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using GameRadar.Core.Xbox;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// Finds the genres of the Steam games, in the Microsoft Store genres used by
/// the Game Pass catalog, from the most to the least reliable source:
/// ① the Game Pass catalog (same name), ② the Microsoft Store (search by
/// exact name), ③ the Steam store page (translated genres). Results are
/// cached for good: a game's genres never change.
/// </summary>
public sealed class SteamGenreSource
{
    // A game found nowhere is retried after this delay only.
    private static readonly TimeSpan RetryNotFoundAfter = TimeSpan.FromDays(30);

    // Gentle with the Microsoft Store (unofficial), and under the Steam
    // store's limit (~200 requests per 5 minutes).
    private static readonly TimeSpan DefaultStoreSearchDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultSteamPageDelay = TimeSpan.FromMilliseconds(1500);

    private readonly CacheStore _cache;
    private readonly string _cacheKey;

    /// <summary>The key of the cache this source reads (see <see cref="CacheCleanup"/>).</summary>
    internal string CacheKey => _cacheKey;
    private readonly TimeSpan _storeSearchDelay;
    private readonly TimeSpan _steamPageDelay;

    public SteamGenreSource(CacheStore cache, string market = "FR")
        : this(cache, market, DefaultStoreSearchDelay, DefaultSteamPageDelay)
    {
    }

    /// <summary>Tests pass no delay: they talk to a fake server.</summary>
    internal SteamGenreSource(CacheStore cache, string market, TimeSpan storeSearchDelay, TimeSpan steamPageDelay)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _cacheKey = $"steam-genres-{market.ToLowerInvariant()}-v1";
        _storeSearchDelay = storeSearchDelay;
        _steamPageDelay = steamPageDelay;
    }

    /// <summary>
    /// Returns the Steam games that say something about the player's taste
    /// (achievements or play time), with their genres. Only games missing
    /// from the local cache cost requests. Never throws when a store fails
    /// (<see cref="StoreLookup"/>): those games are looked up next time.
    /// </summary>
    /// <param name="progress">Receives (done, total) during the lookups.</param>
    public async Task<IReadOnlyList<PlayedGameWithGenres>> ResolveAsync(
        IEnumerable<PlayedGame> history,
        CatalogMatcher matcher,
        GamePassClient store,
        SteamStoreClient steamStore,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var known = LoadEntries();

        // Every game but Xbox (StoreGenreSource has those), added by hand too: found by
        // their name the same way (Game Pass catalog, Microsoft Store), then by
        // their Steam page when they have an app id. Remembered by platform id:
        // GOG ids (~1.2 billion) never meet Steam app ids (under 10 million).
        var games = history
            .Where(g => g.Platform != XboxLiveClient.PlatformName
                && g.HasEngagementSignal)
            .ToList();

        var toLookUp = games
            .Where(g => matcher.Find(g) is null
                && (!known.TryGetValue(g.PlatformId, out var entry)
                    || (entry.Genres.Count == 0 && now - entry.CheckedAt > RetryNotFoundAfter)))
            .ToList();

        // The Steam store's rate limit, a store down...: the games not reached
        // yet stay unknown and are looked up at the next refresh.
        if (toLookUp.Count > 0)
            await StoreLookup.RunAsync(_cache, _cacheKey, known,
                answers => LookUpAsync(toLookUp, answers, store, steamStore, progress, now, ct),
                SteamJsonContext.Default.DictionaryStringGenreEntry, ct).ConfigureAwait(false);

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

    private async Task LookUpAsync(
        List<PlayedGame> games,
        Dictionary<string, GenreEntry> answers,
        GamePassClient store,
        SteamStoreClient steamStore,
        IProgress<(int Done, int Total)>? progress,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Step ②: name -> Microsoft Store product, one search per game.
        var productIds = new Dictionary<string, string>();
        for (int i = 0; i < games.Count; i++)
        {
            progress?.Report((i, games.Count));
            if (i > 0)
                await Task.Delay(_storeSearchDelay, ct).ConfigureAwait(false);
            if (await store.SearchProductIdAsync(games[i].Name, ct).ConfigureAwait(false) is { } productId)
                productIds[games[i].PlatformId] = productId;
        }

        // Product -> genres and modes, 200 products per request.
        var features = await store.FetchFeaturesAsync(productIds.Values, ct).ConfigureAwait(false);
        var missing = new List<PlayedGame>();
        foreach (var game in games)
        {
            if (productIds.TryGetValue(game.PlatformId, out var productId) && features.GetValueOrDefault(productId) is { } found)
                answers[game.PlatformId] = new GenreEntry(found.Genres, found.Modes, now);
            else
                missing.Add(game);
        }

        // Step ③: the Steam store page, slowly (rate limit).
        for (int i = 0; i < missing.Count; i++)
        {
            progress?.Report((games.Count - missing.Count + i, games.Count));
            // A PlayStation game, or one added by hand found nowhere on Steam, has no page to read.
            string? appId = missing[i].SteamAppId;
            if (appId is null)
            {
                answers[missing[i].PlatformId] = new GenreEntry([], PlayModes.None, now);
                continue;
            }
            if (i > 0)
                await Task.Delay(_steamPageDelay, ct).ConfigureAwait(false);
            var found = await steamStore.FetchFeaturesAsync(appId, ct).ConfigureAwait(false);
            // Remembered even when not found, to be retried in 30 days only.
            answers[missing[i].PlatformId] = new GenreEntry(found?.Genres ?? [], found?.Modes ?? PlayModes.None, now);
        }
        progress?.Report((games.Count, games.Count));
    }

    private Dictionary<string, GenreEntry> LoadEntries() => _cache.LoadDictionary(_cacheKey, SteamJsonContext.Default.DictionaryStringGenreEntry);

    /// <summary>Cached result for one Steam appid. Empty genres = found nowhere.</summary>
    internal sealed record GenreEntry(IReadOnlyList<string> Genres, PlayModes Modes, DateTimeOffset CheckedAt);
}
