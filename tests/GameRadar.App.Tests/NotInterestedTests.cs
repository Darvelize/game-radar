using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameRadar.App.ViewModels;
using GameRadar.App.Views;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

/// <summary>
/// "Pas intéressé" on a card: saved, announced with a way back, listed in the
/// preferences. What it hides is the engine's rule (SimilarityRecommender,
/// NotInterestedTests of the Core).
/// </summary>
public sealed class NotInterestedTests : AppTest
{
    private static RecommendationRow Card(string title) =>
        new(new SimilarGame(new Recommendation(
            new CatalogGame("id-" + title, title, null, null, ["Shooter"], OnPc: true, OnConsole: true, LeavingSoon: false), 0.5, []),
            0.9, ["Shooter"], PlayModes.None));

    [AvaloniaFact]
    public async Task A_refusal_is_saved_by_title_and_announced()
    {
        var vm = SampleData.Empty(TempFolder);

        vm.NotInterested.RefuseCommand.Execute(Card("Call of Duty®: Black Ops 6"));

        Assert.True(vm.NotInterested.HasLastRefusal);
        Assert.Contains("Call of Duty®: Black Ops 6", vm.NotInterested.LastRefusalLabel);
        Assert.Equal(["Call of Duty®: Black Ops 6"], vm.NotInterested.Games.Select(g => g.Title));
        // Saved under the title key: the same game on the Game Pass or on Steam.
        var saved = await SavedSettingsAsync(vm);
        Assert.Equal(GameTitle.Key("Call of Duty: Black Ops 6"), Assert.Single(saved.NotInterested).Key);
    }

    [AvaloniaFact]
    public async Task Undo_gives_the_game_back()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.NotInterested.RefuseCommand.Execute(Card("Halo Infinite"));

        vm.NotInterested.UndoCommand.Execute(null);

        Assert.False(vm.NotInterested.HasLastRefusal);
        Assert.False(vm.NotInterested.HasGames);
        Assert.Empty((await SavedSettingsAsync(vm)).NotInterested);
    }

    [AvaloniaFact]
    public void A_game_is_given_back_from_the_preferences()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.NotInterested.RefuseCommand.Execute(Card("Halo Infinite"));
        vm.NotInterested.RefuseCommand.Execute(Card("Forza Horizon 5"));

        vm.NotInterested.RestoreCommand.Execute(vm.NotInterested.Games.Single(g => g.Title == "Forza Horizon 5"));

        Assert.Equal(["Halo Infinite"], vm.NotInterested.Games.Select(g => g.Title));
        // It was the last refusal: its banner goes away with it.
        Assert.False(vm.NotInterested.HasLastRefusal);
    }

    [AvaloniaFact]
    public async Task The_refused_games_are_listed_at_startup_sorted_by_title()
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.NotInterested[GameTitle.Key("Starfield")] = "Starfield";
        settings.NotInterested[GameTitle.Key("Call of Duty")] = "Call of Duty";
        await store.SaveAsync(settings);

        var vm = SampleData.Empty(TempFolder);

        Assert.Equal(["Call of Duty", "Starfield"], vm.NotInterested.Games.Select(g => g.Title));
        // Nothing just refused: no banner.
        Assert.False(vm.NotInterested.HasLastRefusal);
    }

    [AvaloniaFact]
    public void Every_card_has_its_not_interested_button()
    {
        // Bound through the page, like the card itself: a wrong ancestor
        // type would bind nothing, silently.
        var vm = SampleData.ViewModel(TempFolder);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("dismiss")).ToList();

        Assert.Equal(vm.Recommendations.Sections.Sum(s => s.Games.Count), buttons.Count);
        Assert.All(buttons, b => Assert.Same(vm.NotInterested.RefuseCommand, b.Command));
        window.Close();
    }
}
