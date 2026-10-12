using GameRadar.App.ViewModels;
using GameRadar.Core.Models;

namespace GameRadar.App.Tests;

public sealed class GameGroupRowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static PlayedGame Xbox(string name, double progress) =>
        new("Xbox", name, name, true, 1, null, progress, Now.AddDays(-60), PlayStatus.Played);

    private static PlayedGame Steam(string name, int minutes) =>
        new("Steam", name, name, false, 0, null, 0, Now.AddDays(-2), PlayStatus.Played, PlaytimeMinutes: minutes);

    private static GameGroupRow Group(
        PlayedGame[] games, HashSet<string>? excluded = null, HashSet<string>? included = null, Action<GameGroupRow>? onChange = null) =>
        new(games.Select(g => (g, (CatalogGame?)null)).ToList(),
            excluded ?? [], included ?? [], new HashSet<string>(), Now, onChange ?? (_ => { }));

    [Fact]
    public void Barely_played_game_is_left_out_of_the_taste_by_default()
    {
        var row = Group([Xbox("Starfield", 2)]);

        Assert.False(row.CountsInTaste);
        Assert.StartsWith("Ignoré d'office", row.TasteTip);
    }

    [Fact]
    public void Barely_played_game_the_player_ticked_counts()
    {
        var starfield = Xbox("Starfield", 2);

        Assert.True(Group([starfield], included: [starfield.Key]).CountsInTaste);
    }

    [Fact]
    public void Game_counts_when_one_of_its_platforms_does()
    {
        // Barely tried on Xbox, 50 h on Steam.
        var row = Group([Xbox("Warframe", 1), Steam("Warframe", 3000)]);

        Assert.True(row.CountsInTaste);
    }

    [Fact]
    public void Marking_a_barely_played_game_finished_makes_it_count_without_a_second_save()
    {
        int saves = 0;
        var row = Group([Xbox("Short Story", 3)], onChange: _ => saves++);

        row.IsCompleted = true;

        // The heart follows the rule; one save only, for the "finished" mark.
        Assert.True(row.CountsInTaste);
        Assert.Equal(1, saves);
    }
}
