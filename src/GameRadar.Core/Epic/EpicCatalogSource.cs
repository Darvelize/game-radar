using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.Core.Epic;

/// <summary>
/// Gives the Epic store candidates, from the local cache or from Epic.
/// Like the other catalogs, cached in compact parsed form and refreshed when
/// older than a week.
/// </summary>
public sealed class EpicCatalogSource
{
    // Version suffix: bumped when the translation (genres, filters) changes,
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "epic-catalog-fr-v1";

    private readonly CompactCatalogCache _cache;

    public EpicCatalogSource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = new CompactCatalogCache(cache, CacheKey);
    }

    /// <summary>The last saved catalog, or null. No network call; never throws.</summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>Fetches the store's collections and ratings, and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(EpicCatalogClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = await client.FetchCatalogAsync(ct).ConfigureAwait(false);

        // Nothing left after parsing: the format changed. Keep the old cache.
        if (games.Count == 0)
            throw new EpicException(CoreStrings.Epic_EmptyCatalog);

        return await _cache.SaveAsync(games, ct).ConfigureAwait(false);
    }
}
