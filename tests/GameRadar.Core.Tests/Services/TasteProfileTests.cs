using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class TasteProfileTests
{
    [Theory]
    [InlineData(100.0, PlayStatus.Mastered, 3.0)]
    [InlineData(20.0, PlayStatus.Completed, 3.0)]
    [InlineData(60.0, PlayStatus.Played, 2.0)]
    [InlineData(30.0, PlayStatus.Played, 1.5)]
    [InlineData(10.0, PlayStatus.Played, 0.5)]
    public void Engagement_FollowsCompletionScale(double progress, PlayStatus status, double expected)
    {
        Assert.Equal(expected, TasteProfile.Engagement(Played("1", progress, status), Now));
    }

    // Steam game without achievements: play time thresholds chosen by the
    // user (20 h / 10 h / 2 h), never 3 without a manual "Histoire finie".
    [Theory]
    [InlineData(100 * 60, 2.0)]
    [InlineData(20 * 60, 2.0)]
    [InlineData(10 * 60, 1.5)]
    [InlineData(2 * 60, 0.5)]
    [InlineData(90, -0.5)]
    public void Engagement_UsesPlayTimeWhenGameHasNoAchievements(int minutes, double expected)
    {
        var game = Played("1", hasAchievements: false, platform: "Steam", lastPlayed: Now.AddDays(-60))
            with { PlaytimeMinutes = minutes };

        Assert.Equal(expected, TasteProfile.Engagement(game, Now));
    }

    // The better of achievements and play time (the user's choice): a player
    // who ignores achievements but plays for hours loves the game all the
    // same (on their Steam library, Skyrim: 274 h for 45 %; Grim Dawn: 60 h
    // for 5 %), and many achievements in few hours count too.
    [Theory]
    [InlineData(30.0, 100 * 60, 2.0)]   // many hours, few achievements
    [InlineData(4.0, 43 * 60, 2.0)]     // under 5 %: was ignored, the hours count
    [InlineData(60.0, 3 * 60, 2.0)]     // many achievements, few hours
    [InlineData(30.0, 12 * 60, 1.5)]    // both agree
    [InlineData(2.0, 90, -0.5)]         // under both bars: dropped
    public void Engagement_TakesTheBetterOfAchievementsAndPlayTime(double progress, int minutes, double expected)
    {
        var game = Played("1", progress: progress, platform: "Steam", lastPlayed: Now.AddDays(-60))
            with { PlaytimeMinutes = minutes };

        Assert.Equal(expected, TasteProfile.Engagement(game, Now));
    }

    [Fact]
    public void Engagement_NeverReachesThreeWithPlayTimeAlone()
    {
        // Only a platinum or a manual "Histoire finie" says a game was finished.
        var game = Played("1", progress: 40, platform: "Steam") with { PlaytimeMinutes = 500 * 60 };

        Assert.Equal(2, TasteProfile.Engagement(game, Now));
    }

    [Fact]
    public void Engagement_MarkedFinishedCountsFullyWithoutAchievements()
    {
        var game = Played("1", status: PlayStatus.Completed, hasAchievements: false, platform: "Steam")
            with { PlaytimeMinutes = 300 };

        Assert.Equal(3, TasteProfile.Engagement(game, Now));
    }

    [Fact]
    public void Engagement_PenalizesGameDroppedLongAgo()
    {
        var dropped = Played("1", progress: 2, lastPlayed: Now.AddDays(-31));

        Assert.Equal(-0.5, TasteProfile.Engagement(dropped, Now));
    }

    [Fact]
    public void Engagement_IsNeutralForGameStartedRecently()
    {
        // Under 5% but played 10 days ago: maybe just started, not dropped.
        var justStarted = Played("1", progress: 2, lastPlayed: Now.AddDays(-10));

        Assert.Equal(0, TasteProfile.Engagement(justStarted, Now));
    }

    [Fact]
    public void Build_LearnsSteamTagsLikeGenresWeightedByTheirVotes()
    {
        // The user's choice: Steam tags are far finer than the Store genres.
        // A tag weighs its relative votes on the game: "Open World" 1 on the
        // mastered game, 0.5 on the one played at 60 %.
        const int openWorld = 1695, roguelike = 1716;
        var profile = TasteProfile.Build(
        [
            new PlayedGameWithGenres(Played("1", 100, PlayStatus.Mastered), [], Tags: new Dictionary<int, double> { [openWorld] = 1 }),
            new PlayedGameWithGenres(Played("2", 60), [], Tags: new Dictionary<int, double> { [openWorld] = 0.5, [roguelike] = 1 })
        ], Now);

        Assert.True(profile.HasTags);
        Assert.Equal((3 * 1 + 2 * 0.5) / (1.5 + TasteProfile.Smoothing), profile.AffinityOfTag(openWorld), precision: 6);
        Assert.Equal(2 / (1 + TasteProfile.Smoothing), profile.AffinityOfTag(roguelike), precision: 6);
        Assert.Equal(2, profile.StrongGamesWithTag(openWorld));
        Assert.Equal(0, profile.AffinityOfTag(9999));
    }

    [Fact]
    public void Build_SmoothsAffinityTowardsZero()
    {
        // One mastered game: 3 / (1 + 10 neutral games).
        var profile = TasteProfile.Build([WithGenres(Played("1", 100, PlayStatus.Mastered), "Shooter")], Now);

        Assert.Equal(3.0 / (1 + TasteProfile.Smoothing), profile.AffinityOf("Shooter"), precision: 6);
    }

    [Fact]
    public void Build_UsesMeanNotSum()
    {
        // 10 games at engagement 2 and 40 games at engagement 2: same taste,
        // the bigger genre only gets more confidence (less smoothing).
        var small = Enumerable.Range(0, 10).Select(i => WithGenres(Played($"s{i}", 60), "Small"));
        var big = Enumerable.Range(0, 40).Select(i => WithGenres(Played($"b{i}", 60), "Big"));

        var profile = TasteProfile.Build(small.Concat(big), Now);

        Assert.Equal(20.0 / 20, profile.AffinityOf("Small"), precision: 6);
        Assert.Equal(80.0 / 50, profile.AffinityOf("Big"), precision: 6);
        Assert.True(profile.AffinityOf("Big") < 2, "a mean can never exceed the engagement itself");
    }

    [Fact]
    public void Build_CountsSecondaryGenresHalf()
    {
        var profile = TasteProfile.Build([WithGenres(Played("1", 60), "Main", "Secondary")], Now);

        Assert.Equal(2.0 / (1 + TasteProfile.Smoothing), profile.AffinityOf("Main"), precision: 6);
        Assert.Equal(1.0 / (0.5 + TasteProfile.Smoothing), profile.AffinityOf("Secondary"), precision: 6);
    }

    [Fact]
    public void StrongGamesIn_CountsOnlyGamesPlayedFar()
    {
        var profile = TasteProfile.Build(
        [
            WithGenres(Played("far", 30), "Shooter"),     // 1.5: strong
            WithGenres(Played("little", 10), "Shooter")   // 0.5: not strong
        ], Now);

        Assert.Equal(1, profile.StrongGamesIn("Shooter"));
    }

    [Fact]
    public void AffinityOf_IsZeroForUnknownGenre()
    {
        Assert.Equal(0, TasteProfile.Build([], Now).AffinityOf("Never played"));
    }
}
