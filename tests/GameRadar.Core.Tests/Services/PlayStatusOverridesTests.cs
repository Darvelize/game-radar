using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class PlayStatusOverridesTests
{
    [Fact]
    public void Apply_RaisesMarkedPlayedGameToCompleted()
    {
        var game = Played("1", progress: 20);

        var result = PlayStatusOverrides.Apply([game], new HashSet<string> { game.Key });

        Assert.Equal(PlayStatus.Completed, Assert.Single(result).Status);
    }

    [Fact]
    public void Apply_NeverLowersMasteredGame()
    {
        var game = Played("1", progress: 100, status: PlayStatus.Mastered);

        var result = PlayStatusOverrides.Apply([game], new HashSet<string> { game.Key });

        Assert.Equal(PlayStatus.Mastered, Assert.Single(result).Status);
    }

    [Fact]
    public void Apply_LeavesUnmarkedGamesAndOriginalsUntouched()
    {
        var marked = Played("1");
        var unmarked = Played("2");

        var result = PlayStatusOverrides.Apply([marked, unmarked], new HashSet<string> { marked.Key });

        Assert.Equal(PlayStatus.Played, result[1].Status);
        // Records are immutable: "with" made a copy, the input is unchanged.
        Assert.Equal(PlayStatus.Played, marked.Status);
    }
}
