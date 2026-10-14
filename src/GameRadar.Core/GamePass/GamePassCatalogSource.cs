using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.Core.GamePass;

/// <summary>
/// Gives the Game Pass catalog, from the local cache or from Microsoft.
/// </summary>
/// <remarks>
/// Unlike the Xbox history, the cache holds the compact parsed catalog, not
/// the raw answers: those weigh ~47 MB, and these endpoints have no quota,
/// so after a parsing change a simple refresh is enough.
/// </remarks>
public sealed class GamePassCatalogSource
{
    private readonly CompactCatalogCache _cache;

    /// <summary>The key of the cache this source reads (see <see cref="CacheCleanup"/>).</summary>
    internal string CacheKey => _cache.Key;

    public GamePassCatalogSource(CacheStore cache, string market = "FR")
    {
        ArgumentNullException.ThrowIfNull(cache);
        // One cache per market: the catalog differs from one country to another.
        // The version suffix changes whenever CatalogGame gains fields, so an
        // old cache (without them) is ignored and refetched rather than
        // silently read with empty values (v6: English titles).
        _cache = new CompactCatalogCache(cache, $"gamepass-catalog-{market.ToLowerInvariant()}-v6");
    }

    /// <summary>
    /// Returns the last saved catalog, or null when there is none.
    /// No network call. Never throws: a corrupted cache counts as missing.
    /// </summary>
    public GameCatalog? LoadCached() => _cache.Load();

    /// <summary>The plans (per platform) that include at least one of these games.</summary>
    private static HashSet<(bool Pc, GamePassTier Tier)> PlansWithGames(IEnumerable<CatalogGame> games) =>
        games
            .SelectMany(g => Enum.GetValues<GamePassTier>()
                .Where(tier => tier != GamePassTier.None)
                .SelectMany(tier => new[] { (Pc: false, Tier: tier), (Pc: true, Tier: tier) })
                .Where(plan => (plan.Pc ? g.PcTiers : g.ConsoleTiers).HasFlag(plan.Tier)))
            .ToHashSet();

    /// <summary>Fetches the whole catalog from Microsoft and replaces the cache.</summary>
    public async Task<GameCatalog> RefreshAsync(GamePassClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var games = await client.FetchCatalogAsync(ct).ConfigureAwait(false);

        // An empty catalog means the answer was not what we expected:
        // better keep the previous cache than overwrite it with nothing.
        // Same for a plan that had games and has none now: its list answered
        // only its header (an id or a subscription changed). Saved, every
        // game of that plan would be out of the Game Pass for a week.
        return await _cache.SaveAsync(games,
            () => new GamePassException(CoreStrings.GamePass_EmptyCatalog),
            previous => PlansWithGames(previous.Games).Except(PlansWithGames(games)).Any()
                ? new GamePassException(CoreStrings.GamePass_MissingPlan) : null,
            ct).ConfigureAwait(false);
    }
}
