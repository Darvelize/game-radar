using GameRadar.App.ViewModels;
using GameRadar.Core.Models;

namespace GameRadar.App.Tests;

/// <summary>The "finished" flag of a history line: ticked by the player, or by the platform.</summary>
public sealed class CompletedFlagTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static GameGroupRow Group(params PlayedGame[] games) =>
        new(games.Select(g => (g, (CatalogGame?)null)).ToList(),
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), Now, onUserChoiceChanged: _ => { });

    private static PlayedGame Game(string platform, string id, PlayStatus status, double progress) =>
        new(platform, id, "Space Heroes", true, 10, 20, progress, Now.AddDays(-1), status, PlaytimeMinutes: 600);

    [Fact]
    public void A_game_finished_according_to_one_platform_shows_the_flag_ticked_everywhere()
    {
        // Platinum without the DLC on PlayStation, also played on Xbox.
        var row = Group(Game("PlayStation", "CUSA1", PlayStatus.Completed, 62), Game("Xbox", "1", PlayStatus.Played, 30));

        Assert.True(row.IsCompletedOnPlatform);
        // Finished is finished: no flag to tick for the Xbox line.
        Assert.False(row.CanMarkCompleted);
    }

    [Fact]
    public void A_mastered_game_shows_no_flag_at_all()
    {
        var row = Group(Game("PlayStation", "CUSA1", PlayStatus.Completed, 62), Game("Steam", "2", PlayStatus.Mastered, 100));

        Assert.False(row.IsCompletedOnPlatform);
        Assert.False(row.CanMarkCompleted);
    }

    [Fact]
    public void A_game_the_platform_does_not_call_finished_keeps_the_flag_to_tick()
    {
        var row = Group(Game("PlayStation", "CUSA1", PlayStatus.Played, 40));

        Assert.False(row.IsCompletedOnPlatform);
        Assert.True(row.CanMarkCompleted);
    }
}
