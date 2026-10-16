using System.Text.Json;
using GameRadar.Core.Models;

namespace GameRadar.Core.Cache;

/// <summary>
/// The free games of one source (the "Jeux gratuits" page), cached in compact
/// parsed form like the catalogs (<see cref="CompactCatalogCache"/>). Offers last
/// days, not weeks: older than a day, or as soon as one of its offers ended, the
/// list is asked again.
/// </summary>
public sealed class FreeGameCache(CacheStore cache, string key)
{
    /// <summary>The cache keys of the sources (see <see cref="CacheCleanup"/>).</summary>
    public const string EpicKey = "free-games-epic-v1";
    public const string PsPlusKey = "free-games-psplus-v1";
    public const string AmazonKey = "free-games-amazon-v1";
    public const string SteamKey = "free-games-steam-v1";

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    // An empty list may be the source lagging behind its change of offers (Epic's
    // CDN right after Thursday 17:00): asked again sooner.
    private static readonly TimeSpan EmptyMaxAge = TimeSpan.FromHours(1);

    /// <summary>The last saved list, or null. No network call; never throws: a corrupted cache counts as missing.</summary>
    public FreeGameList? Load() => cache.LoadJson(key, CoreJsonContext.Default.FreeGameList);

    /// <summary>Missing, older than a day (an hour when empty), or with an offer that ended (the next games are out).</summary>
    public static bool NeedsDownload(FreeGameList? list, DateTimeOffset now) =>
        list is null
        || now - list.FetchedAt > (list.Games.Count == 0 ? EmptyMaxAge : MaxAge)
        || list.Games.Any(g => g.Until <= now);

    /// <summary>The games still free at <paramref name="now"/> (one without end date stays).</summary>
    public static IReadOnlyList<FreeGame> Current(FreeGameList? list, DateTimeOffset now) =>
        list?.Games.Where(g => g.Until is null || g.Until > now).ToList() ?? [];

    /// <summary>Replaces the cache with these games, fetched now. An empty list is kept: a week may have no gift.</summary>
    public async Task<FreeGameList> SaveAsync(IReadOnlyList<FreeGame> games, CancellationToken ct = default)
    {
        var list = new FreeGameList(games, DateTimeOffset.UtcNow);
        await cache.SaveAsync(key, JsonSerializer.Serialize(list, CoreJsonContext.Default.FreeGameList), ct).ConfigureAwait(false);
        return list;
    }
}
