using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

public sealed class RecommendationFilterTests : AppTest
{
    [AvaloniaFact]
    public void Starting_the_app_keeps_both_saved_boxes()
    {
        // Found on a screenshot: loading the first box saved the second one
        // before it was loaded, so "Steam" came back unticked at each start.
        var vm = SampleData.Empty(TempFolder);

        Assert.True(vm.SourceFilter.ShowGamePass);
        Assert.True(vm.SourceFilter.ShowSteam);
    }

    [AvaloniaFact]
    public async Task Choices_are_remembered_after_a_restart()
    {
        var store = new SettingsStore(TempFolder);
        var saved = store.Load();
        saved.ShowSteam = false;
        saved.GamePassPlan = GamePassTier.Premium;
        await store.SaveAsync(saved);

        var vm = SampleData.Empty(TempFolder);

        Assert.True(vm.SourceFilter.ShowGamePass);
        Assert.False(vm.SourceFilter.ShowSteam);
        Assert.Equal("Game Pass Premium", vm.SourceFilter.GamePassLabel);
        Assert.True(vm.SourceFilter.HasGamePassPlan);
    }

    [AvaloniaFact]
    public async Task The_ticked_sources_are_those_usable_in_the_order_of_the_merge()
    {
        var store = new SettingsStore(TempFolder);
        var saved = store.Load();
        saved.GamePassPlan = GamePassTier.None;
        saved.PsPlusPlan = PsPlusTier.Extra;
        saved.ShowGog = false;
        saved.ShowPlayStationStore = true;
        await store.SaveAsync(saved);

        var vm = SampleData.Empty(TempFolder);

        // The Game Pass box is ticked, but without a plan it offers nothing.
        Assert.True(vm.SourceFilter.ShowGamePass);
        Assert.Equal(
            [CatalogSource.PsPlus, CatalogSource.Steam, CatalogSource.Epic, CatalogSource.PlayStationStore],
            vm.SourceFilter.Ticked);
        Assert.Equal("PlayStation Plus Extra", vm.SourceFilter.Label(CatalogSource.PsPlus));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_combo_box_left_empty_keeps_the_choice_through_KeepChoice(bool keepChoice)
    {
        var vm = SampleData.Empty(TempFolder);
        var chosen = vm.SourceFilter.SelectedGamePassPlan;
        var combo = new ComboBox { DataContext = vm.SourceFilter, ItemsSource = vm.SourceFilter.GamePassPlans };
        combo.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(vm.SourceFilter.SelectedGamePassPlan))
        {
            Mode = BindingMode.TwoWay,
            Converter = keepChoice ? KeepChoice.Instance : null
        });
        new Window { Content = combo }.Show();
        Assert.Same(chosen, combo.SelectedItem);

        // Its items replaced (a recycled line gets them again): it writes null.
        try
        {
            combo.ItemsSource = null;
        }
        catch (NullReferenceException) when (!keepChoice)
        {
            // The premise: without the converter, the null reaches the hook.
        }

        if (keepChoice)
            Assert.Same(chosen, vm.SourceFilter.SelectedGamePassPlan);
        else
            Assert.Null(vm.SourceFilter.SelectedGamePassPlan);
    }

    [Fact]
    public void Every_combo_box_selection_goes_through_KeepChoice()
    {
        // The guard is on the binding, not in the hooks: a new list must have it too.
        var views = Path.Combine(SampleData.RepositoryRoot(), "src", "GameRadar.App", "Views");
        var selections = Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"<ComboBox\b[^>]*?SelectedItem=""([^""]*)""").Select(m => (file, m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(selections);
        Assert.All(selections, s => Assert.Contains("KeepChoice", s.Value));
    }

    [AvaloniaFact]
    public void A_click_on_a_card_opens_its_store_page()
    {
        Uri? opened = null;
        var vm = SampleData.Empty(TempFolder, uri => opened = uri);
        var game = new CatalogGame("steam:397540", "Borderlands 3", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, SteamAppId: "397540");
        var row = new RecommendationRow(new SimilarGame(new Recommendation(game, 0.5, []), 0.9, ["Shooter"], PlayModes.None));

        vm.Recommendations.OpenPageCommand.Execute(row.SteamPage);

        Assert.Equal(new Uri("https://store.steampowered.com/app/397540/?l=french"), opened);
    }
}
