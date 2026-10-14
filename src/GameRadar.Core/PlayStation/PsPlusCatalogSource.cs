using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Resources;
using GameRadar.Core.Services;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// Gives the PS Plus catalog, from the local cache or from playstation.com.
/// </summary>
/// <remarks>
/// Like the Game Pass catalog, the cache holds the compact parsed catalog
/// (~100 KB), and these lists have no quota: after a parsing change a simple
/// refresh is enough.
/// </remarks>
public sealed class PsPlusCatalogSource
{
    private readonly CompactCatalogCache _cache;

    /// <summary>The key of the cache this source reads (see <see cref="CacheCleanup"/>).</summary>
    internal string CacheKey => _cache.Key;

    public PsPlusCatalogSource(CacheStore cache, string locale = "fr-fr")
    {
        ArgumentNullException.ThrowIfNull(cache);
        // One cache per country: the catalog differs from one to another. The
        // version suffix changes whenever CatalogGame gains fields.
        _cache = new CompactCatalogCache(cache, $"psplus-catalog-{locale.ToLowerInvariant()}-v1");
    }

    /// <summary>
    /// Returns the last saved catalog, or null when there is none.
    /// No network call. Never throws: a corrupted cache counts as missing.
    /// </summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>Fetches the whole catalog from playstation.com and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(PsPlusCatalogClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = OnePerTitle(await client.FetchCatalogAsync(ct).ConfigureAwait(false));

        // An empty catalog means the answer was not what we expected:
        // better keep the previous cache than overwrite it with nothing.
        // Same for a plan that lost its games (as for the Game Pass, see
        // GamePassCatalogSource). Plans nest, so each combination of plans is
        // a plan of its own here (Extra + Premium, Premium alone).
        return await _cache.SaveAsync(games,
            () => new PlayStationException(CoreStrings.PsPlus_EmptyCatalog),
            previous => Combinations(previous.Games).Except(Combinations(games)).Any()
                ? new PlayStationException(CoreStrings.PsPlus_MissingPlan) : null,
            ct).ConfigureAwait(false);
    }

    private static HashSet<PsPlusTier> Combinations(IEnumerable<CatalogGame> games) =>
        games.Select(g => g.PsPlusTiers).ToHashSet();

    // A game sold for PS4 and PS5 can be two concepts ("Dead Cells" and "Dead
    // Cells PS4 & PS5"): one candidate per title (GameTitle, the rule everywhere),
    // in every plan that has one of them.
    private static List<CatalogGame> OnePerTitle(IEnumerable<CatalogGame> games)
    {
        var byTitle = new Dictionary<string, CatalogGame>();
        foreach (var game in games)
        {
            string key = GameTitle.Key(game.Title);
            byTitle[key] = byTitle.TryGetValue(key, out var known)
                ? known with { PsPlusTiers = known.PsPlusTiers | game.PsPlusTiers }
                : game;
        }
        return byTitle.Values.ToList();
    }
}
