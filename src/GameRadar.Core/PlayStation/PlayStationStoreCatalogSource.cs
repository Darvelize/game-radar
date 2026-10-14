using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Resources;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// Gives the PlayStation Store candidates, from the local cache or from the
/// store. Like the other catalogs, cached in compact parsed form and refreshed
/// when older than a week.
/// </summary>
public sealed class PlayStationStoreCatalogSource
{
    // Version suffix: bumped when the translation (genres, filters) changes,
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "psstore-catalog-fr-v1";

    private readonly CompactCatalogCache _cache;

    public PlayStationStoreCatalogSource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = new CompactCatalogCache(cache, CacheKey);
    }

    /// <summary>The last saved catalog, or null. No network call; never throws.</summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>Fetches the store's best sellers and their pages, and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(PlayStationStoreClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = await client.FetchCatalogAsync(ct).ConfigureAwait(false);

        // Nothing left after parsing: the format changed. Keep the old cache.
        return await _cache.SaveAsync(games, () => new PlayStationException(CoreStrings.PsStore_EmptyCatalog), ct: ct).ConfigureAwait(false);
    }
}
