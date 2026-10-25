using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameRadar.App.ViewModels;
using GameRadar.App.Views;

namespace GameRadar.App.Tests;

/// <summary>
/// The window: a DrawerPage (sidebar) around a TabbedPage whose hidden tabs
/// are the pages, driven by the view model.
/// </summary>
public sealed class NavigationTests : AppTest
{
    [AvaloniaTheory]
    [InlineData(AppPage.Recommendations, typeof(RecommendationsView))]
    [InlineData(AppPage.FreeGames, typeof(FreeGamesView))]
    [InlineData(AppPage.History, typeof(HistoryView))]
    [InlineData(AppPage.Settings, typeof(SettingsView))]
    [InlineData(AppPage.WhatsNew, typeof(WhatsNewView))]
    [InlineData(AppPage.Terms, typeof(TermsView))]
    [InlineData(AppPage.Licenses, typeof(LicensesView))]
    public void The_window_shows_the_page_of_the_view_model(AppPage page, Type view)
    {
        var vm = SampleData.ViewModel(TempFolder);
        var window = Open(vm);

        vm.NavigateCommand.Execute(page);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType(view, Pages(window).SelectedPage);
        window.Close();
    }

    [AvaloniaFact]
    public void The_pages_have_no_visible_tab()
    {
        // The sidebar navigates: the TabbedPage's own tabs must not show.
        var window = Open(SampleData.ViewModel(TempFolder));

        var tabs = Pages(window).GetVisualDescendants().OfType<TabItem>().ToList();

        Assert.NotEmpty(tabs);
        Assert.All(tabs, t => Assert.False(t.IsEffectivelyVisible));
        window.Close();
    }

    [AvaloniaFact]
    public void Every_platform_badge_of_a_card_opens_its_page()
    {
        // A badge reaches the page's command through its ContentPage: a
        // wrong ancestor type binds nothing, silently (no error, no click).
        var vm = SampleData.ViewModel(TempFolder);
        var window = Open(vm);

        var badges = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("badgeLink") && b.IsVisible).ToList();

        Assert.NotEmpty(badges);
        Assert.All(badges, b => Assert.Same(vm.OpenPageCommand, b.Command));
        Assert.All(badges, b => Assert.IsType<Uri>(b.CommandParameter));
        window.Close();
    }

    private static Window Open(MainWindowViewModel vm)
    {
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // The TabbedPage of the pages (the settings have their own, with tabs).
    private static TabbedPage Pages(Window window) =>
        window.GetVisualDescendants().OfType<TabbedPage>().Single(p => p.Classes.Contains("hiddenTabs"));
}
