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
    /// Replaces the cache with these games, fetched now, unless the answer was
    /// not what we expected: never save over a valid cache what it gave.
    /// </summary>
    /// <param name="whenEmpty">The connector's error for an empty catalog (the format changed).</param>
    /// <param name="refuseAgainst">
    /// Checked after the emptiness, against the saved catalog (if any): the error
    /// for an answer that lost part of it (a plan with no game left), or null.
    /// </param>
    public async Task<GameCatalog> SaveAsync(
        IReadOnlyList<CatalogGame> games,
        Func<Exception> whenEmpty,
        Func<GameCatalog, Exception?>? refuseAgainst = null,
        CancellationToken ct = default)
    {
        if (games.Count == 0)
            throw whenEmpty();
        if (refuseAgainst is not null && Load() is { } previous && refuseAgainst(previous) is { } refused)
            throw refused;

        var catalog = new GameCatalog(games, DateTimeOffset.UtcNow);
        await cache.SaveAsync(Key, JsonSerializer.Serialize(catalog, CoreJsonContext.Default.GameCatalog), ct).ConfigureAwait(false);
        return catalog;
    }
}
