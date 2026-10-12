using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameRadar.App.ViewModels;
using GameRadar.App.Views;

namespace GameRadar.App.Tests;

public sealed class SettingsTabsTests : AppTest
{
    [AvaloniaFact]
    public void The_settings_open_on_the_accounts()
    {
        var vm = SampleData.Empty(TempFolder);

        vm.NavigateCommand.Execute(AppPage.Settings);

        Assert.Equal(SettingsTab.Accounts, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void The_tab_is_remembered_while_the_app_runs()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.NavigateCommand.Execute(AppPage.Settings);
        vm.SettingsTabIndex = (int)SettingsTab.Privacy;

        vm.NavigateCommand.Execute(AppPage.History);
        vm.NavigateCommand.Execute(AppPage.Settings);

        Assert.Equal(SettingsTab.Privacy, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void A_link_opens_the_settings_on_its_tab()
    {
        // "Ajoute ta bibliothèque dans les Réglages", from the welcome screen.
        var vm = SampleData.Empty(TempFolder);
        vm.CurrentSettingsTab = SettingsTab.About;

        vm.OpenSettingsCommand.Execute(SettingsTab.Accounts);

        Assert.True(vm.IsSettingsPage);
        Assert.Equal(SettingsTab.Accounts, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void Back_from_the_release_notes_returns_to_the_about_tab()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.OpenSettingsCommand.Execute(SettingsTab.About);

        vm.NavigateCommand.Execute(AppPage.WhatsNew);
        vm.GoBackCommand.Execute(null);

        Assert.True(vm.IsSettingsPage);
        Assert.Equal(SettingsTab.About, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void No_tab_while_the_page_is_built_keeps_the_current_one()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.CurrentSettingsTab = SettingsTab.Data;

        vm.SettingsTabIndex = -1;

        Assert.Equal(SettingsTab.Data, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void The_tabbed_page_and_the_view_model_show_the_same_tab()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.OpenSettingsCommand.Execute(SettingsTab.Preferences);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var tabs = SettingsTabs(window);

        // The view model decides first (a link from elsewhere)...
        Assert.Equal((int)SettingsTab.Preferences, tabs.SelectedIndex);
        // ...then a click on a tab reaches the view model.
        tabs.SelectedIndex = (int)SettingsTab.Privacy;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SettingsTab.Privacy, vm.CurrentSettingsTab);
        // One tab per value of the enum, in the same order.
        Assert.Equal(Enum.GetValues<SettingsTab>().Length, tabs.Pages!.Count());

        window.Close();
    }

    [AvaloniaFact]
    public void The_arrows_change_tab_only_from_a_tab()
    {
        var vm = SampleData.ViewModel(TempFolder);
        vm.CurrentPage = AppPage.Settings;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var tabs = SettingsTabs(window);

        // Focus on a button of the page ("Synchroniser"): → stays on the tab.
        tabs.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible).Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SettingsTab.Accounts, vm.CurrentSettingsTab);

        // Focus on the tab itself: → moves to the next one.
        tabs.GetVisualDescendants().OfType<TabItem>().First().Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SettingsTab.Preferences, vm.CurrentSettingsTab);

        window.Close();
    }

    // The one of the settings page (the window holds others: its pages, the terms of use).
    private static TabbedPage SettingsTabs(Window window) =>
        window.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<TabbedPage>().Single();
}
