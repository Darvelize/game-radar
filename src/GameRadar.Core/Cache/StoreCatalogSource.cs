using GameRadar.Core.Models;

namespace GameRadar.Core.Cache;

/// <summary>
/// The candidates of a store (Steam, GOG, Epic, PlayStation Store), from the local
/// cache or from the store: cached in compact parsed form like the Game Pass
/// catalog (<see cref="CompactCatalogCache"/>) and refreshed when older than a
/// week. Each connector gives its cache key and its error for an empty catalog.
/// </summary>
public sealed class StoreCatalogSource
{
    private readonly CompactCatalogCache _cache;
    private readonly Func<Exception> _whenEmpty;

    public StoreCatalogSource(CacheStore cache, string cacheKey, Func<Exception> whenEmpty)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = new CompactCatalogCache(cache, cacheKey);
        _whenEmpty = whenEmpty;
    }

    /// <summary>The last saved catalog, or null. No network call; never throws.</summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>Fetches the store's candidates and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(IStoreCatalogClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = await client.FetchCatalogAsync(ct).ConfigureAwait(false);

        // Nothing left after parsing: the format changed. Keep the old cache.
        return await _cache.SaveAsync(games, _whenEmpty, ct: ct).ConfigureAwait(false);
    }
}

/// <summary>The client of a store's candidates (<see cref="StoreCatalogSource"/>).</summary>
public interface IStoreCatalogClient
{
    Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default);
}
