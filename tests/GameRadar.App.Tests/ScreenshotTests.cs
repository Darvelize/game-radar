using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameRadar.App.ViewModels;
using GameRadar.App.Views;
using GameRadar.Core.Manual;
using GameRadar.Core.Updates;

namespace GameRadar.App.Tests;

/// <summary>
/// Renders each screen in light and dark themes to PNG files, so the design
/// can be checked without opening the app. Output: bin/.../screenshots/.
/// </summary>
public sealed class ScreenshotTests : AppTest
{
    private static readonly string OutputFolder = Path.Combine(AppContext.BaseDirectory, "screenshots");

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_page(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        var window = Open(vm, theme);

        foreach (var page in Enum.GetValues<AppPage>())
        {
            vm.CurrentPage = page;
            Save(window, $"{page}-{theme}");
        }

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void First_launch_with_an_error(string theme)
    {
        var vm = SampleData.Empty(TempFolder);
        vm.Notices.Show("Ta connexion Microsoft a expiré : reconnecte-toi.");
        var window = Open(vm, theme);

        Save(window, $"Welcome-{theme}");
        // Settings with no account: the Steam form is visible.
        vm.CurrentPage = AppPage.Settings;
        Save(window, $"Settings-signed-out-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void New_version_available(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.AvailableUpdate = new AvailableUpdate(new Version(1, 1, 0), UpdateChecker.DownloadPage);
        var window = Open(vm, theme);

        Save(window, $"Update-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Language_list_open(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.Settings;
        vm.CurrentSettingsTab = SettingsTab.Preferences;
        vm.Language.SelectedLanguage = vm.Language.LanguageOptions[1];
        var window = Open(vm, theme);
        Save(window, $"Language-closed-{theme}");

        // The list pops up above the page: open it, then capture.
        var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemsSource == vm.Language.LanguageOptions);
        combo.IsDropDownOpen = true;
        Save(window, $"Language-open-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void History_filter_open(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.History;
        vm.History.PlatformFilter.Choices.Single(c => c.Label == "Steam").IsChecked = false;
        var window = Open(vm, theme);

        // The platform picklist pops its check boxes up: open it, then capture.
        var picklist = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("picklist"));
        picklist.Flyout!.ShowAt(picklist);
        Save(window, $"History-filter-open-{theme}");

        // Its boxes are bound to the view model (the Flyout's content is outside the page's tree).
        var boxes = picklist.Flyout is Flyout { Content: Control content }
            ? content.GetVisualDescendants().OfType<CheckBox>().ToList()
            : [];
        Assert.False(boxes.Single(b => Equals(b.Content, "Steam")).IsChecked);
        // First, the box for every platform: unticked, as one is.
        Assert.Equal("Toutes", boxes[0].Content);
        Assert.False(boxes[0].IsChecked);
        Assert.Equal("Xbox, PlayStation, GOG, EA, Epic, Ajoutés à la main", vm.History.PlatformFilter.Summary);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void History_tag_panel_open(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.History;
        vm.History.Games[0].TagEditor.Text = "Coop";
        var window = Open(vm, theme);

        // The × of a tag the player added: no width at rest (the badges sit as close
        // as Steam's), open once Tab reaches it, as when the badge is hovered.
        var crosses = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("removeTag") && b.IsVisible).ToList();
        Assert.All(crosses, c => Assert.Equal(0, c.Bounds.Width));
        crosses[0].Focus();
        window.UpdateLayout();
        Assert.True(crosses[0].Bounds.Width > 0);
        Assert.Equal(0, crosses[1].Bounds.Width);
        Save(window, $"History-tag-cross-{theme}");

        // The "+" of the first row pops its search up: open it, then capture.
        var plus = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("badgeLink") && b.Flyout is not null);
        plus.Flyout!.ShowAt(plus);
        Save(window, $"History-tag-panel-{theme}");

        // The panel is bound to its row (the Flyout's content is outside the page's tree).
        var box = plus.Flyout is Flyout { Content: Control content }
            ? content.GetVisualDescendants().OfType<AutoCompleteBox>().Single()
            : null;
        Assert.Equal("Coop", box!.Text);
        Assert.NotEmpty(box.ItemsSource!.Cast<object>());

        // Its suggestions, as typing shows them.
        box.Focus();
        box.Text = "Co";
        box.IsDropDownOpen = true;
        Save(window, $"History-tag-suggestions-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Free_games(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        var window = Open(vm, theme, height: 1300);

        vm.CurrentPage = AppPage.FreeGames;
        Assert.True(await BecomesTrueAsync(() => vm.FreeGames.Sections.All(s => s.Games.Count > 0), 5000));
        SampleData.PaintFreeGames(vm);
        // A game the player already has (no history here: set on the card).
        vm.FreeGames.Sections[0].Games.First().InHistory = true;

        Save(window, $"FreeGames-cards-{theme}");
        // The second tab: the games free for a while.
        window.GetVisualDescendants().OfType<Views.FreeGamesView>().Single()
            .GetVisualDescendants().OfType<TabbedPage>().First().SelectedIndex = 1;
        Save(window, $"FreeGames-timed-{theme}");
        window.Close();
    }

    [AvaloniaFact]
    public void Every_page_in_English()
    {
        // The views read their texts once, when built: switch the language first.
        TestCulture.In("en-GB", () =>
        {
            var vm = SampleData.ViewModel(TempFolder);
            vm.Privacy.IsConfirmingErase = true;
            var window = Open(vm, "Light", height: 2000);
            foreach (var page in Enum.GetValues<AppPage>())
            {
                vm.CurrentPage = page;
                Save(window, $"{page}-en");
            }
            // Every settings tab: the longest English labels live there.
            vm.CurrentPage = AppPage.Settings;
            foreach (var tab in Enum.GetValues<SettingsTab>())
            {
                vm.CurrentSettingsTab = tab;
                Save(window, $"Settings-{tab}-en");
            }
            window.Close();
        });
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void News_after_an_update(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.WhatsNew.HasWhatsNew = true;
        // Every version of the repository's CHANGELOG.md, the one in
        // preparation too (the app hides it until released): its parts
        // ("Évolutions", "Corrections") are seen before the release. The
        // newest version marked "Nouveau".
        vm.WhatsNew.ReleaseNotes = Changelog.Parse(File.ReadAllText(Path.Combine(SampleData.RepositoryRoot(), "CHANGELOG.md")))
            .Select((e, i) => new ReleaseNotesRow(e, isNew: i == 0)).ToList();
        var window = Open(vm, theme, height: 1100);

        Save(window, $"News-banner-{theme}");
        vm.CurrentPage = AppPage.WhatsNew;
        Save(window, $"News-page-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Add_a_game(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.AddGame;
        // As after a search (no network here): on both stores, on Steam, on the Xbox and PlayStation stores, as typed.
        vm.AddGame.Confirmation = "« Bloodborne » est ajouté à ton historique.";
        vm.AddGame.Text = "hollow knight";
        vm.AddGame.Results.Add(new AddGameResultRow(new ManualGameMatch("Hollow Knight", "367520", InGamePass: true, OnSteam: true)));
        vm.AddGame.Results.Add(new AddGameResultRow(new ManualGameMatch("Hollow Knight: Silksong", "1030300", InGamePass: false, OnSteam: true)));
        vm.AddGame.Results.Add(new AddGameResultRow(new ManualGameMatch("Halo 3", null, InGamePass: false, OnSteam: false, OnXbox: true)));
        vm.AddGame.Results.Add(new AddGameResultRow(new ManualGameMatch("Astro Bot", null, InGamePass: false, OnSteam: false, OnPlayStation: true)));
        vm.AddGame.Results.Add(AddGameResultRow.AsTyped("hollow knight voidheart"));
        vm.AddGame.SelectedResult = vm.AddGame.Results[0];
        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.Switch);
        var window = Open(vm, theme);

        Save(window, $"AddGame-results-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Not_interested(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        var window = Open(vm, theme);

        // The mouse over the first card: its "Pas intéressé" button shows.
        var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card") && b.Classes.Contains("media"));
        var center = card.TranslatePoint(new Point(card.Bounds.Width / 2, 60), window)!.Value;
        window.MouseMove(center, RawInputModifiers.None);
        Save(window, $"NotInterested-hover-{theme}");

        // Refused: the banner with "Annuler" (the card itself goes away with a
        // real computation, which the sample data has not).
        vm.NotInterested.RefuseCommand.Execute(vm.Recommendations.Sections[1].Games[2]);
        window.MouseMove(new Point(5, 5), RawInputModifiers.None);
        Save(window, $"NotInterested-banner-{theme}");

        vm.NotInterested.RefuseCommand.Execute(vm.Recommendations.Sections[1].Games[0]);
        vm.CurrentPage = AppPage.Settings;
        vm.CurrentSettingsTab = SettingsTab.Preferences;
        Save(window, $"NotInterested-preferences-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Legal_texts(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        // Tall: the folded sections come below the list of components.
        var window = Open(vm, theme, height: 1400);

        vm.Legal.OpenTermsCommand.Execute(null);
        Save(window, $"Legal-terms-{theme}");
        vm.Legal.TermsTabIndex = 1;
        Save(window, $"Legal-terms-english-{theme}");

        vm.Legal.OpenThirdPartyNoticesCommand.Execute(null);
        Save(window, $"Legal-licenses-{theme}");
        vm.Legal.NoticeSections[0].IsExpanded = true;
        Save(window, $"Legal-licenses-mit-{theme}");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Every_settings_tab(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.Settings;
        vm.Privacy.IsConfirmingErase = true;
        var window = Open(vm, theme);

        foreach (var tab in Enum.GetValues<SettingsTab>())
        {
            vm.CurrentSettingsTab = tab;
            Save(window, $"Settings-{tab}-{theme}");
        }
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Recommendations_sources_menu_open(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.Recommendations;
        var window = Open(vm, theme);

        // "Sources (7)": its boxes in a menu, open it, then capture.
        var sources = window.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is not null && !b.Classes.Contains("badgeLink"));
        sources.Flyout!.ShowAt(sources);
        Save(window, $"Recommendations-sources-{theme}");

        Assert.Equal("Sources (7)", vm.SourceFilter.SourcesLabel);
        // Its boxes are bound to the view model (the Flyout's content is outside the page's tree).
        var boxes = sources.Flyout is Flyout { Content: Control content }
            ? content.GetVisualDescendants().OfType<CheckBox>().ToList()
            : [];
        Assert.True(boxes.Single(b => Equals(b.Content, "Pas encore terminés")).IsChecked);
        Assert.Equal("1 375", vm.Recommendations.Counts.Steam.Replace('\u202F', ' ').Replace('\u00A0', ' '));

        // Its footer is a link to the plans: Settings › Accounts, the menu closed.
        var link = ((Control)((Flyout)sources.Flyout!).Content!).GetVisualDescendants().OfType<HyperlinkButton>().Single();
        // A click: the Click event first, then the command (Button.OnClick), then the menu closes.
        var command = link.Command!;
        link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        command.Execute(link.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.False(sources.Flyout!.IsOpen);
        Assert.True(vm.IsSettingsPage);
        Assert.Equal(SettingsTab.Accounts, vm.CurrentSettingsTab);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Recommendations_down_to_the_PS_Plus_cards(string theme)
    {
        // A tall window: the PS Plus cards (a game only in PS Plus, one also on Steam) are the last
        // section, with a game only on Epic, one only on the PlayStation Store and one only on GOG.
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.Recommendations;
        var window = Open(vm, theme, height: 2000);

        Save(window, $"Recommendations-tall-{theme}");
        window.Close();

        var psPlusCards = vm.Recommendations.Sections.SelectMany(s => s.Games).Where(c => c.InPsPlus).ToList();
        Assert.Equal(["Horizon Zero Dawn", "Dead Cells"], psPlusCards.Select(c => c.Title));
        Assert.Equal("Bloodborne", Assert.Single(vm.Recommendations.Sections.SelectMany(s => s.Games), c => c.IsOnPsStore).Title);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void History_down_to_the_PlayStation_GOG_and_EA_lines(string theme)
    {
        // A tall window: the platinum without DLC (flag ticked by the platform), the GOG and EA lines come last.
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.History;
        var window = Open(vm, theme, height: 1400);

        Save(window, $"History-tall-{theme}");
        window.Close();

        Assert.True(vm.History.Games.Single(r => r.Name == "Marvel's Spider-Man").IsCompletedOnPlatform);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Accounts_tab_down_to_the_PS_Plus_plan(string theme)
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.Settings;
        vm.CurrentSettingsTab = SettingsTab.Accounts;
        var window = Open(vm, theme, height: 2150);

        Save(window, $"Settings-accounts-tall-{theme}");
        window.Close();

        Assert.Equal("PlayStation Plus Extra", vm.SourceFilter.SelectedPsPlusPlan.Label);
    }

    private static Window Open(MainWindowViewModel vm, string theme, double height = 800)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = height };
        window.Show();
        return window;
    }

    private static void Save(Window window, string name)
    {
        // Lets the page fade-in animation (200 ms) run to its end: each
        // forced tick advances the headless clock by one frame.
        for (int i = 0; i < 30; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        Directory.CreateDirectory(OutputFolder);
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(OutputFolder, name.ToLowerInvariant() + ".png"), new PngBitmapEncoderOptions());
    }
}
