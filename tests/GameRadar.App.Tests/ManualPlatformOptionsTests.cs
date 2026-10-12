using GameRadar.App.ViewModels;
using GameRadar.Core.Manual;

namespace GameRadar.App.Tests;

public class ManualPlatformOptionsTests
{
    [Fact]
    public void EveryPlatformHasAnOption()
    {
        // ManualGameOptions.Of throws for a platform without one: a game saved with it would break the history.
        foreach (var platform in Enum.GetValues<ManualPlatform>())
            Assert.Equal(platform, ManualGameOptions.Of(platform).Platform);
    }

    [Fact]
    public void ThePlatformsAreFamilies()
    {
        var labels = ManualGameOptions.Platforms.Select(o => o.Label).ToList();

        Assert.Equal(["PlayStation", "Xbox", "Nintendo", "PC (autre boutique)", "Android", "iOS", "Autre"], labels);
    }

    [Fact]
    public void TheOldXboxValueIsShownAsXbox()
    {
        Assert.Equal("Xbox", ManualGameOptions.Of(ManualPlatform.OldXbox).Label);
    }
}
