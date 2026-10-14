using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.Core.Steam;

/// <summary>
/// Gives the Steam store candidates, from the local cache or from Steam.
/// Like the Game Pass catalog, cached in compact parsed form (the raw
/// answers weigh ~7 MB) and refreshed when older than a week.
/// </summary>
public sealed class SteamCatalogSource
{
    // Version suffix: bumped when the translation (tags, filters) changes
    // (v2: tags kept for the similarity search; v3: English names),
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "steam-catalog-fr-v3";

    private readonly CompactCatalogCache _cache;

    public SteamCatalogSource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = new CompactCatalogCache(cache, CacheKey);
    }

    /// <summary>The last saved catalog, or null. No network call; never throws.</summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>Fetches the best sellers from Steam and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(SteamCatalogClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = await client.FetchCatalogAsync(ct).ConfigureAwait(false);

        // Nothing left after parsing: the format changed. Keep the old cache.
        return await _cache.SaveAsync(games, () => new SteamException(CoreStrings.Steam_EmptyCatalog), ct: ct).ConfigureAwait(false);
    }
}
