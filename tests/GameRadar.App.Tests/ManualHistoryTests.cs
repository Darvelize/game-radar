using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Manual;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

/// <summary>
/// Games added by hand in the history: shown without any account, edited in
/// place on their line, removed from there.
/// </summary>
public sealed class ManualHistoryTests : AppTest
{
    private async Task<MainWindowViewModel> WithGamesAsync(params ManualGame[] games)
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.AddRange(games);
        await store.SaveAsync(settings);
        return SampleData.Empty(TempFolder);
    }

    [AvaloniaFact]
    public async Task Games_added_by_hand_show_at_once_without_any_account()
    {
        var vm = await WithGamesAsync(new ManualGame("Hollow Knight", ManualPlatform.Switch, ManualLevel.Completed));

        // No Xbox or Steam history: no welcome screen, the game is there.
        Assert.True(vm.History.HasHistory);
        var row = Assert.Single(vm.History.Games);
        Assert.True(row.IsManual);
        Assert.Equal("Nintendo", row.ManualPlatformLabel);
        Assert.Equal("Terminé", row.StatusLabel);
        // Unfolds into its editing line, even on a single platform.
        Assert.True(row.HasDetails);
        Assert.Equal("Terminé", Assert.Single(row.Details).AchievementsLabel);
    }

    [AvaloniaFact]
    public async Task A_game_added_by_hand_wears_the_badge_of_its_platform()
    {
        var vm = await WithGamesAsync(
            new ManualGame("Astro Bot", ManualPlatform.PlayStation, ManualLevel.Mastered),
            new ManualGame("Halo 3", ManualPlatform.OldXbox, ManualLevel.Completed),
            new ManualGame("Hollow Knight", ManualPlatform.Switch, ManualLevel.Completed));

        var playStation = vm.History.Games.Single(r => r.Name == "Astro Bot");
        var xbox = vm.History.Games.Single(r => r.Name == "Halo 3");
        var nintendo = vm.History.Games.Single(r => r.Name == "Hollow Knight");

        // PlayStation blue and Xbox green, as for a synced game; the others stay neutral.
        Assert.True(playStation.ShowManualPlayStationBadge);
        Assert.False(playStation.ShowManualNeutralBadge);
        Assert.True(xbox.ShowManualXboxBadge);
        Assert.False(xbox.ShowManualNeutralBadge);
        Assert.True(nintendo.ShowManualNeutralBadge);
        Assert.False(nintendo.ShowManualPlayStationBadge || nintendo.ShowManualXboxBadge);
    }

    [AvaloniaFact]
    public async Task Didnt_like_it_counts_in_the_taste_by_default()
    {
        var vm = await WithGamesAsync(new ManualGame("Anthem", ManualPlatform.PlayStation, ManualLevel.DidNotLike));

        Assert.True(Assert.Single(vm.History.Games).CountsInTaste);
    }

    [AvaloniaFact]
    public async Task Editing_a_game_replaces_its_line_only_and_keeps_it_unfolded()
    {
        var vm = await WithGamesAsync(
            new ManualGame("Bloodborne", ManualPlatform.PlayStation, ManualLevel.PlayedALot),
            new ManualGame("Zelda", ManualPlatform.Switch, ManualLevel.Mastered));
        var edited = vm.History.Games.Single(r => r.Name == "Bloodborne");
        var other = vm.History.Games.Single(r => r.Name == "Zelda");
        int index = vm.History.Games.IndexOf(edited);
        edited.IsExpanded = true;

        edited.Details[0].Manual!.SelectedLevel = ManualGameOptions.Of(ManualLevel.Completed);

        var fresh = vm.History.Games[index];
        Assert.NotSame(edited, fresh);
        Assert.Equal("Terminé", fresh.StatusLabel);
        Assert.True(fresh.IsExpanded);
        Assert.Same(other, vm.History.Games.Single(r => r.Name == "Zelda"));
        var saved = await SavedSettingsAsync(vm);
        Assert.Equal(ManualLevel.Completed, saved.ManualGames.Single(g => g.Title == "Bloodborne").Level);
    }

    [AvaloniaFact]
    public async Task Removing_the_last_game_leaves_nothing_to_show()
    {
        var vm = await WithGamesAsync(new ManualGame("Bloodborne", ManualPlatform.PlayStation, ManualLevel.PlayedALot));

        vm.History.Games[0].Details[0].Manual!.RemoveCommand.Execute(null);

        Assert.Empty(vm.History.Games);
        Assert.False(vm.History.HasHistory);
        Assert.Empty((await SavedSettingsAsync(vm)).ManualGames);
    }
}
