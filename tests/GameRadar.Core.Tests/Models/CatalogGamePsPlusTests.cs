using System.Text.Json;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Models;

public class CatalogGamePsPlusTests
{
    // Like the real lists: an Extra game is also in Premium, a classic only in Premium.
    private static readonly CatalogGame ExtraGame = Catalog("E1", inGamePass: false) with
    {
        PsPlusTiers = PsPlusTier.Extra | PsPlusTier.Premium
    };

    private static readonly CatalogGame ClassicGame = Catalog("C1", inGamePass: false) with
    {
        PsPlusTiers = PsPlusTier.Premium
    };

    [Fact]
    public void InPsPlus_IsFalseWithoutAnyPlan()
    {
        Assert.False(Catalog("G1").InPsPlus);
    }

    [Fact]
    public void ForPsPlusPlan_KeepsAnExtraGameForExtraAndPremium()
    {
        Assert.True(ExtraGame.ForPsPlusPlan(PsPlusTier.Extra).InPsPlus);
        Assert.True(ExtraGame.ForPsPlusPlan(PsPlusTier.Premium).InPsPlus);
    }

    [Fact]
    public void ForPsPlusPlan_HidesAClassicFromAnExtraSubscriber()
    {
        Assert.False(ClassicGame.ForPsPlusPlan(PsPlusTier.Extra).InPsPlus);
        Assert.True(ClassicGame.ForPsPlusPlan(PsPlusTier.Premium).InPsPlus);
    }

    [Fact]
    public void ForPsPlusPlan_WithoutSubscriptionLeavesNothingInPsPlus()
    {
        Assert.False(ExtraGame.ForPsPlusPlan(PsPlusTier.None).InPsPlus);
    }

    [Fact]
    public void ForPsPlusPlan_DoesNotTouchTheGamePassFields()
    {
        var both = Catalog("B1") with { PsPlusTiers = PsPlusTier.Premium };

        var seen = both.ForPsPlusPlan(PsPlusTier.Extra);

        Assert.True(seen.InGamePass);
        Assert.False(seen.InPsPlus);
    }

    [Fact]
    public void ACacheWrittenBeforePsPlusStillReads()
    {
        // The compact catalog cache of the previous version has no PsPlusTiers.
        const string old = """{"ProductId":"P","Title":"T","XboxTitleId":null,"PackageFamilyName":null,"Genres":[],"OnPc":true,"OnConsole":false,"LeavingSoon":false}""";

        var game = JsonSerializer.Deserialize(old, TestJsonContext.Default.CatalogGame)!;

        Assert.Equal(PsPlusTier.None, game.PsPlusTiers);
        Assert.False(game.InPsPlus);
    }
}
