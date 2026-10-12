using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class HistoryMergerTests
{
    private static PlayedGame SteamGame(string appId, string name, int minutes, double progress = 0, bool hasAchievements = false) =>
        Played(appId, progress, name: name, hasAchievements: hasAchievements, platform: "Steam") with { PlaytimeMinutes = minutes };

    [Fact]
    public void Combine_PutsBothHistoriesTogetherMostRecentFirst()
    {
        var xboxGame = Played("x1", name: "Halo Infinite", lastPlayed: Now.AddDays(-10));
        var steamGame = SteamGame("1091500", "Cyberpunk 2077", 3000) with { LastPlayed = Now.AddDays(-1) };

        var merged = HistoryMerger.Combine([xboxGame], [steamGame]);

        Assert.Equal(["1091500", "x1"], merged.Select(g => g.PlatformId));
    }

    [Fact]
    public void GroupByGame_PutsTheSameGameOnBothPlatformsTogether()
    {
        var onXbox = Played("x1", name: "Warframe", lastPlayed: Now.AddDays(-30));
        var onSteam = SteamGame("230410", "Warframe®", 18_000) with { LastPlayed = Now.AddDays(-2) };
        var other = Played("x2", name: "Halo Infinite", lastPlayed: Now.AddDays(-10));

        var groups = HistoryMerger.GroupByGame([onXbox, other, onSteam]);

        // Warframe first (played 2 days ago on Steam), its Steam line on top.
        Assert.Equal(2, groups.Count);
        Assert.Equal([onSteam, onXbox], groups[0]);
        Assert.Equal([other], groups[1]);
    }

    [Fact]
    public void KeepBestPerGame_CountsAGamePlayedOnBothPlatformsOnce()
    {
        var onXbox = WithGenres(Played("x1", progress: 10, name: "Destiny 2"), "Shooter");               // 0.5
        var onSteam = WithGenres(SteamGame("1085660", "Destiny 2", 90_000, 60, hasAchievements: true), "Shooter"); // 2
        var other = WithGenres(Played("x2", progress: 30, name: "Control"), "Action & adventure");

        var kept = HistoryMerger.KeepBestPerGame([onXbox, onSteam, other], Now);

        Assert.Equal(2, kept.Count);
        Assert.Contains(onSteam, kept);
        Assert.DoesNotContain(onXbox, kept);
    }

    [Fact]
    public void KeepBestPerGame_TakesTheTagsAndGenresKnownOnAnotherPlatform()
    {
        // Played furthest on Xbox, whose title search found no tags; the Steam
        // line (same game) knows them by its app id, but not its genres.
        var onXbox = WithGenres(Played("x1", progress: 60, name: "Section 8: Prejudice"), "Shooter");   // 2
        var onSteam = new PlayedGameWithGenres(
            SteamGame("99700", "Section 8: Prejudice", 300, 10, hasAchievements: true), [],              // 0.5
            Tags: new Dictionary<int, double> { [1774] = 1 });

        var kept = Assert.Single(HistoryMerger.KeepBestPerGame([onXbox, onSteam], Now));

        Assert.Same(onXbox.Game, kept.Game);
        Assert.Equal(["Shooter"], kept.Genres);
        Assert.Same(onSteam.Tags, kept.Tags);
    }
}
