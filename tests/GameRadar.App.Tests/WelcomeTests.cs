using System.ComponentModel;
using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;

namespace GameRadar.App.Tests;

/// <summary>
/// The welcome screen of the first launch: every account is connected in
/// Settings › Accounts, then loading the history comes first.
/// </summary>
public sealed class WelcomeTests : AppTest
{
    [AvaloniaFact]
    public void Connect_my_accounts_opens_the_accounts_tab()
    {
        var vm = SampleData.Empty(TempFolder);
        Assert.False(vm.History.HasHistory);

        vm.OpenSettingsCommand.Execute(SettingsTab.Accounts);

        Assert.Equal(AppPage.Settings, vm.CurrentPage);
        Assert.Equal(SettingsTab.Accounts, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void Once_an_account_is_connected_loading_the_history_is_offered()
    {
        var vm = SampleData.Empty(TempFolder);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm.Accounts).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(vm.Accounts.HasAccount);

        vm.Accounts.PlayStationSync.IsConnected = true;

        // The view is told: "Charger mon historique" shows, as the main button.
        Assert.True(vm.Accounts.HasAccount);
        Assert.Contains(nameof(AccountsViewModel.HasAccount), changed);
        Assert.True(vm.Accounts.RefreshHistoryCommand.CanExecute(null));
    }
}
