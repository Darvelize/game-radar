using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Models;

public class CatalogGameTests
{
    // Like a real catalog entry: plans nest (Essential ⊂ Premium ⊂ Ultimate).
    private static readonly CatalogGame PremiumGame = Catalog("P1") with
    {
        ConsoleTiers = GamePassTier.Premium | GamePassTier.Ultimate,
        PcTiers = GamePassTier.Premium | GamePassTier.Ultimate | GamePassTier.PcGamePass,
        LeavingSoon = true
    };

    [Fact]
    public void ForPlan_KeepsAGameOfThePlan()
    {
        var seen = PremiumGame.ForPlan(GamePassTier.Premium);

        Assert.True(seen.InGamePass && seen.OnConsole && seen.OnPc && seen.LeavingSoon);
    }

    [Fact]
    public void ForPlan_TakesAGameOfAHigherPlanOutOfTheGamePass()
    {
        var seen = PremiumGame.ForPlan(GamePassTier.Essential);

        // Out of the player's Game Pass: not playable, not "leaving" either.
        Assert.False(seen.InGamePass);
        Assert.False(seen.LeavingSoon);
    }

    [Fact]
    public void ForPlan_KeepsOnlyThePcForPcGamePass()
    {
        var seen = PremiumGame.ForPlan(GamePassTier.PcGamePass);

        Assert.True(seen.OnPc);
        Assert.False(seen.OnConsole);
    }

    [Fact]
    public void ForPlan_WithoutSubscriptionLeavesNothingInTheGamePass()
    {
        Assert.False(PremiumGame.ForPlan(GamePassTier.None).InGamePass);
    }

    [Fact]
    public void ForPlan_CountsAGameWithoutKnownPlansAsInEveryPlan()
    {
        // Built without the plan lists (old tests, hand-made data).
        var unknown = Catalog("P2");

        Assert.True(unknown.ForPlan(GamePassTier.Essential).InGamePass);
    }
}
