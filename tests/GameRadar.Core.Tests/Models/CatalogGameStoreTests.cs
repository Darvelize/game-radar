using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Models;

/// <summary>Which source a candidate comes from: the one whose entry the merge kept, and every one that offers it.</summary>
public class CatalogGameStoreTests
{
    private static readonly CatalogGame OnNoStore = Catalog("X", inGamePass: false);
    private const string Gog = "https://www.gog.com/en/game/x";
    private const string Epic = "https://store.epicgames.com/p/x";

    public static TheoryData<CatalogGame, CatalogSource> Primary => new()
    {
        // The order of the merge: subscriptions, then Steam, GOG, Epic, the PlayStation Store.
        { Catalog("GP", steamAppId: "1") with { PsPlusTiers = PsPlusTier.Extra, GogStoreLink = Gog }, CatalogSource.GamePass },
        // A PS Plus game sold on Steam keeps its PS Plus entry (its picture), with Steam's rating.
        { OnNoStore with { PsPlusTiers = PsPlusTier.Extra, SteamAppId = "1" }, CatalogSource.PsPlus },
        { OnNoStore with { SteamAppId = "1", GogStoreLink = Gog, PlayStationProductId = "EP1" }, CatalogSource.Steam },
        { OnNoStore with { GogStoreLink = Gog, EpicStoreLink = Epic }, CatalogSource.Gog },
        { OnNoStore with { EpicStoreLink = Epic, PlayStationProductId = "EP1" }, CatalogSource.Epic },
        { OnNoStore with { PlayStationProductId = "EP1" }, CatalogSource.PlayStationStore },
    };

    [Theory]
    [MemberData(nameof(Primary))]
    public void PrimaryStore_IsTheFirstSourceInTheOrderOfTheMerge(CatalogGame game, CatalogSource expected)
    {
        Assert.Equal(expected, game.PrimaryStore);
    }

    [Fact]
    public void IsIn_SaysEverySourceThatOffersTheGame()
    {
        var game = OnNoStore with { SteamAppId = "1", EpicStoreLink = Epic, PlayStationProductId = "EP1" };

        Assert.Equal(
            [CatalogSource.Steam, CatalogSource.Epic, CatalogSource.PlayStationStore],
            Enum.GetValues<CatalogSource>().Where(game.IsIn));
    }

    [Fact]
    public void IsIn_CountsTheSubscriptionsOnlyForThePlayersPlan()
    {
        var game = Catalog("GP") with { PsPlusTiers = PsPlusTier.Premium };

        Assert.True(game.IsIn(CatalogSource.GamePass));
        Assert.True(game.IsIn(CatalogSource.PsPlus));
        Assert.False(game.ForPlan(GamePassTier.None).IsIn(CatalogSource.GamePass));
        Assert.False(game.ForPsPlusPlan(PsPlusTier.Extra).IsIn(CatalogSource.PsPlus));
    }
}
