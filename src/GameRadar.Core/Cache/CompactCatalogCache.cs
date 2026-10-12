using System.Text.Json;
using GameRadar.Core.Models;

namespace GameRadar.Core.Cache;

/// <summary>
/// A candidate catalog (Game Pass, PS Plus, Steam, GOG) cached in compact parsed
/// form rather than raw: the raw answers weigh megabytes (~47 MB for the Game
/// Pass) and these endpoints have no quota, so after a parsing change a refresh
/// (or a new version suffix of the key) is enough. Refreshed when older than a week.
/// </summary>
public sealed class CompactCatalogCache(CacheStore cache, string key)
{
    /// <summary>Catalogs and best sellers move slowly: older than this, refresh.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>The key of the cache (see <see cref="CacheCleanup"/>).</summary>
    public string Key { get; } = key;

    /// <summary>The last saved catalog, or null. No network call; never throws: a corrupted cache counts as missing.</summary>
    public GameCatalog? Load() => cache.LoadJson(Key, CoreJsonContext.Default.GameCatalog);

    public static bool IsStale(GameCatalog catalog, DateTimeOffset now) =>
        now - catalog.FetchedAt > MaxAge;

    /// <summary>
    /// Replaces the cache with these games, fetched now. The caller checks them
    /// first (empty, a plan missing): never save over a valid cache what an
    /// unexpected answer gave.
    /// </summary>
    public async Task<GameCatalog> SaveAsync(IReadOnlyList<CatalogGame> games, CancellationToken ct = default)
    {
        var catalog = new GameCatalog(games, DateTimeOffset.UtcNow);
        await cache.SaveAsync(Key, JsonSerializer.Serialize(catalog, CoreJsonContext.Default.GameCatalog), ct).ConfigureAwait(false);
        return catalog;
    }
}
