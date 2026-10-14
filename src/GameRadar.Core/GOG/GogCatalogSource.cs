using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.Core.GOG;

/// <summary>
/// Gives the GOG store candidates, from the local cache or from GOG.
/// Like the Steam catalog, cached in compact parsed form (the raw answers
/// weigh ~3.7 MB) and refreshed when older than a week.
/// </summary>
public sealed class GogCatalogSource
{
    // Version suffix: bumped when the translation (genres, filters) changes,
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "gog-catalog-fr-v1";

    private readonly CompactCatalogCache _cache;

    public GogCatalogSource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = new CompactCatalogCache(cache, CacheKey);
    }

    /// <summary>The last saved catalog, or null. No network call; never throws.</summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>Fetches the best sellers from GOG and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(GogCatalogClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = await client.FetchCatalogAsync(ct).ConfigureAwait(false);

        // Nothing left after parsing: the format changed. Keep the old cache.
        return await _cache.SaveAsync(games, () => new GogException(CoreStrings.Gog_EmptyCatalog), ct: ct).ConfigureAwait(false);
    }
}
