using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using GameRadar.Core.Steam;

namespace GameRadar.App.Tests;

/// <summary>
/// What the tests of a candidate source (PS Plus, PlayStation Store) start from:
/// a fresh Game Pass catalog with the game added by hand, an empty Steam store,
/// and the titles of the cards.
/// </summary>
internal static class CachedCatalogs
{
    public static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    public static readonly CatalogGame HeroInGamePass =
        new("GP-HERO", "Hero Quest", null, null, ["Role playing"], OnPc: true, OnConsole: true, LeavingSoon: false);

    public static async Task SaveGamePassAndSteamAsync(CacheStore cache)
    {
        await cache.SaveAsync(new GamePassCatalogSource(cache).CacheKey, TestJson.Serialize(new GameCatalog([HeroInGamePass], DateTimeOffset.UtcNow - Fresh)));
        await cache.SaveAsync(SteamCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([], DateTimeOffset.UtcNow - Fresh)));
    }

    public static IEnumerable<string> Cards(MainWindowViewModel vm) =>
        vm.Recommendations.Sections.SelectMany(s => s.Games).Select(g => g.Title);
}
