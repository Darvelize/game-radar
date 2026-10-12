using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

/// <summary>
/// The user's choice: games launched but barely played are left out of the
/// taste by default (on Game Pass, launching a game says nothing), and come
/// in by themselves once the player gets further.
/// </summary>
public class TasteSelectionTests
{
    private static readonly HashSet<string> None = [];

    private static PlayedGame SteamGame(int minutes) =>
        Played("1", hasAchievements: false, platform: "Steam") with { PlaytimeMinutes = minutes };

    [Theory]
    [InlineData(4.9, false)]
    [InlineData(5, true)]
    public void CountsByDefault_LeavesOutGamesUnderFivePercent(double progress, bool expected)
    {
        Assert.Equal(expected, TasteSelection.CountsByDefault(Played("x", progress)));
    }

    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    public void CountsByDefault_LeavesOutSteamGamesUnderTwoHours(int minutes, bool expected)
    {
        Assert.Equal(expected, TasteSelection.CountsByDefault(SteamGame(minutes)));
    }

    [Theory]
    [InlineData(119, false)]
    [InlineData(120, true)]
    public void CountsByDefault_CountsAGameUnderFivePercentPlayedForHours(int minutes, bool expected)
    {
        // Barely played only under both bars: few achievements but hours of play counts.
        var game = Played("1", progress: 3, platform: "Steam") with { PlaytimeMinutes = minutes };

        Assert.Equal(expected, TasteSelection.CountsByDefault(game));
    }

    [Fact]
    public void CountsByDefault_KeepsABarelyPlayedGameMarkedFinished()
    {
        // The manual mark applied (PlayStatusOverrides): the story was finished.
        Assert.True(TasteSelection.CountsByDefault(Played("x", 2, PlayStatus.Completed)));
    }

    [Fact]
    public void Counts_ComesInByItselfOnceThePlayerGetsFurther()
    {
        // Same game, nothing ticked: the rule follows the progress.
        Assert.False(TasteSelection.Counts(Played("x", 3), None, None));
        Assert.True(TasteSelection.Counts(Played("x", 12), None, None));
    }

    [Fact]
    public void Counts_ThePlayersChoiceWinsOverTheRule()
    {
        var barelyPlayed = Played("x1", 2);
        var wellPlayed = Played("x2", 60);

        Assert.True(TasteSelection.Counts(barelyPlayed, None, new HashSet<string> { barelyPlayed.Key }));
        Assert.False(TasteSelection.Counts(wellPlayed, new HashSet<string> { wellPlayed.Key }, None));
    }

    [Fact]
    public void Counts_NeverAGameWithoutAnySignal()
    {
        // Owned on Steam but never launched: nothing to learn from, even if ticked.
        var neverPlayed = SteamGame(0);

        Assert.False(TasteSelection.Counts(neverPlayed, None, new HashSet<string> { neverPlayed.Key }));
    }
}
