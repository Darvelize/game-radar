using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

/// <summary>
/// Sort and filters of the history: within a group the ticked boxes add up,
/// the groups narrow each other, "Tout afficher" undoes everything.
/// </summary>
public sealed class HistoryFilterTests : AppTest
{
    private static readonly ManualGame Zelda = new("Zelda", ManualPlatform.Switch, ManualLevel.Completed);
    private static readonly ManualGame Astro = new("Astro Bot", ManualPlatform.PlayStation, ManualLevel.Mastered);
    private static readonly ManualGame Bayonetta = new("Bayonetta", ManualPlatform.Switch, ManualLevel.PlayedALittle);
    private async Task<MainWindowViewModel> ViewModelAsync()
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        // Added in this order: the history shows them as they come.
        settings.ManualGames.AddRange([Zelda, Astro, Bayonetta]);
        // Left out of the taste by the player.
        settings.ExcludedFromTaste.Add(ManualGames.ToPlayed(Bayonetta).Key);
        await store.SaveAsync(settings);

        // A PlayStation game too, from its cache (no network: everything answers 404).
        Directory.CreateDirectory(Path.Combine(TempFolder, "cache"));
        await File.WriteAllTextAsync(Path.Combine(TempFolder, "cache", "playstation-history.json"), """
            { "trophyTitles": [ { "trophyTitles": [
                { "npCommunicationId": "NPWR00001_00", "trophyTitleName": "Space Heroes", "progress": 30,
                  "earnedTrophies": { "bronze": 10, "silver": 0, "gold": 0, "platinum": 0 },
                  "definedTrophies": { "bronze": 30, "silver": 10, "gold": 5, "platinum": 1 },
                  "lastUpdatedDateTime": "2026-08-01T20:00:00Z" } ], "totalItemCount": 1 } ],
              "games": [ { "titles": [], "totalItemCount": 0 } ] }
            """);
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        await vm.InitializeAsync();
        return vm;
    }

    private static string[] Names(MainWindowViewModel vm) => vm.History.Games.Select(r => r.Name).ToArray();

    // A picklist, by its title (French: the tests run in fr-FR).
    private static HistoryFilterGroup Group(MainWindowViewModel vm, string title) => vm.History.Filters.Single(g => g.Title == title);

    // A box of a picklist, by its label (French: the tests run in fr-FR).
    private static HistoryFilterChoice Box(HistoryFilterGroup group, string label) => group.Choices.Single(c => c.Label == label);

    [AvaloniaFact]
    public async Task Only_a_row_with_details_unfolds()
    {
        var vm = await ViewModelAsync();
        // Space Heroes: one platform, nothing to unfold (a screen reader may still toggle its name).
        var single = vm.History.AllRows.Single(r => r.Name == "Space Heroes");
        var manual = vm.History.AllRows.Single(r => r.Name == "Zelda");

        single.IsExpanded = true;
        manual.IsExpanded = true;

        Assert.False(single.HasDetails);
        Assert.False(single.IsExpanded);
        Assert.True(manual.IsExpanded);
    }

    [AvaloniaFact]
    public async Task Unticking_a_heart_with_Ignored_hidden_keeps_the_row_after_the_recomputation()
    {
        // The documented choice: no row vanishes under the mouse; the recomputation the
        // click starts shares the tags, which refiltered the list. A catalog, so that it runs.
        await CachedCatalogs.SaveGamePassAndSteamAsync(new CacheStore(Path.Combine(TempFolder, "cache")));
        var vm = await ViewModelAsync();
        Box(Group(vm, "Goûts :"), "Ignorés").IsChecked = false;
        var row = vm.History.Games.First(r => r.CountsInTaste);

        row.CountsInTaste = false;
        await vm.Recommendations.UpdateAsync();

        Assert.Contains(row, vm.History.Games);
    }

    [AvaloniaFact]
    public async Task The_summary_counts_as_finished_the_games_the_Finished_box_shows()
    {
        // Zelda, added by hand at "Terminé": one finished, as the box says.
        var vm = await ViewModelAsync();

        Box(Group(vm, "Statut :"), "Platiné").IsChecked = false;
        Box(Group(vm, "Statut :"), "Joué").IsChecked = false;

        Assert.Equal(["Zelda"], Names(vm));
        Assert.Equal("4 jeux, dont 1 platinés, 1 terminés.", vm.History.Summary);
    }

    [AvaloniaFact]
    public async Task Removing_the_last_game_added_by_hand_takes_its_box_away()
    {
        var vm = await ViewModelAsync();

        foreach (var editor in vm.History.AllRows.SelectMany(r => r.Details).Select(d => d.Manual).OfType<ManualGameEditor>().ToList())
            editor.RemoveCommand.Execute(null);

        Assert.False(Box(vm.History.PlatformFilter, "Ajoutés à la main").IsOffered);
        Assert.True(Box(vm.History.PlatformFilter, "PlayStation").IsOffered);
    }

    [AvaloniaFact]
    public async Task Sorting_from_A_to_Z_follows_the_names()
    {
        var vm = await ViewModelAsync();

        vm.History.SelectedSort = vm.History.SortOptions.Single(o => o.Sort == HistorySort.Alphabetical);

        Assert.Equal(["Astro Bot", "Bayonetta", "Space Heroes", "Zelda"], Names(vm));
    }

    [AvaloniaFact]
    public async Task Only_the_platforms_present_get_a_box_and_unticking_one_hides_its_games()
    {
        var vm = await ViewModelAsync();
        Assert.True(Box(vm.History.PlatformFilter, "PlayStation").IsOffered);
        Assert.True(Box(vm.History.PlatformFilter, "Ajoutés à la main").IsOffered);
        Assert.False(Box(vm.History.PlatformFilter, "Xbox").IsOffered);

        Assert.Equal("Toutes", vm.History.PlatformFilter.Summary);

        Box(vm.History.PlatformFilter, "Ajoutés à la main").IsChecked = false;

        Assert.Equal(["Space Heroes"], Names(vm));
        // The closed picklist sums up the ticked platforms, among those offered.
        Assert.Equal("PlayStation", vm.History.PlatformFilter.Summary);
        Assert.True(vm.History.IsFiltered);
        Assert.Equal("1 jeux affichés sur 4", vm.History.FilterStatus);
    }

    [AvaloniaFact]
    public async Task Status_and_taste_boxes_narrow_each_other()
    {
        var vm = await ViewModelAsync();

        // Not mastered nor finished: only the games just played.
        Box(Group(vm, "Statut :"), "Platiné").IsChecked = false;
        Box(Group(vm, "Statut :"), "Terminé").IsChecked = false;
        Assert.Equal(["Bayonetta", "Space Heroes"], Names(vm).Order());

        // ...and among them, the one left out of the taste.
        Box(Group(vm, "Goûts :"), "Comptent").IsChecked = false;
        Assert.Equal(["Bayonetta"], Names(vm));
    }

    [AvaloniaFact]
    public async Task Show_all_ticks_every_box_again()
    {
        var vm = await ViewModelAsync();
        Box(vm.History.PlatformFilter, "PlayStation").IsChecked = false;
        Box(Group(vm, "Goûts :"), "Ignorés").IsChecked = false;

        vm.History.ShowAllCommand.Execute(null);

        Assert.False(vm.History.IsFiltered);
        Assert.Equal(4, vm.History.Games.Count);
    }

    [AvaloniaFact]
    public async Task The_first_box_unticks_every_platform_then_ticks_them_all_again()
    {
        var vm = await ViewModelAsync();
        var platforms = vm.History.PlatformFilter;
        // Every box ticked: so is the first one ("Toutes").
        Assert.True(platforms.AllChecked);

        platforms.AllChecked = false;

        Assert.Empty(Names(vm));
        Assert.Equal("Aucune", platforms.Summary);
        // A platform without games keeps its box ticked: its games show once synced.
        Assert.True(Box(platforms, "Xbox").IsChecked);

        platforms.AllChecked = true;

        Assert.Equal(4, vm.History.Games.Count);
        Assert.Equal("Toutes", platforms.Summary);
    }

    [AvaloniaFact]
    public async Task The_first_box_follows_the_others_and_ticks_them_all_when_some_are_unticked()
    {
        var vm = await ViewModelAsync();
        var platforms = vm.History.PlatformFilter;

        Box(platforms, "PlayStation").IsChecked = false;
        // Some unticked: the first box is not ticked, a click ticks them all.
        Assert.False(platforms.AllChecked);

        platforms.AllChecked = true;

        Assert.True(Box(platforms, "PlayStation").IsChecked);
        Assert.Equal(4, vm.History.Games.Count);
    }

    [Fact]
    public void Ticking_every_box_at_once_filters_the_list_once()
    {
        int filtered = 0;
        var group = new HistoryFilterGroup("Plateforme :", "Toutes", "Aucune", () => filtered++,
            ("Xbox", _ => true), ("Steam", _ => true), ("GOG", _ => true));

        group.AllChecked = false;

        Assert.Equal(1, filtered);
        Assert.All(group.Choices, c => Assert.False(c.IsChecked));
    }
}
