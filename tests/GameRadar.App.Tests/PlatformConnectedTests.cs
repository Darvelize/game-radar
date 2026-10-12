using System.ComponentModel;
using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;

namespace GameRadar.App.Tests;

/// <summary>
/// A platform carries its own "connected" state (SyncedPlatform.IsConnected):
/// the welcome screen, the sync button and its sidebar line follow it, with
/// no flag per platform in AccountsViewModel.
/// </summary>
public sealed class PlatformConnectedTests : AppTest
{
    // 0 = Xbox, 1 = Steam, 2 = PlayStation, in the order of SyncedPlatforms.
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Connecting_a_platform_offers_the_history_and_disconnecting_takes_it_back(int index)
    {
        var vm = SampleData.Empty(TempFolder);
        var platform = vm.Accounts.SyncedPlatforms[index];
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm.Accounts).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(vm.Accounts.HasAccount);

        platform.IsConnected = true;

        Assert.True(vm.Accounts.HasAccount);
        Assert.Contains(nameof(AccountsViewModel.HasAccount), changed);
        Assert.True(vm.Accounts.RefreshHistoryCommand.CanExecute(null));

        platform.IsConnected = false;

        Assert.False(vm.Accounts.HasAccount);
        Assert.False(vm.Accounts.RefreshHistoryCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_connected_platform_shows_its_sidebar_line_without_waiting_for_a_refresh(int index)
    {
        var vm = SampleData.Empty(TempFolder);
        var platform = vm.Accounts.SyncedPlatforms[index];
        Assert.False(platform.ShowSync);

        platform.IsConnected = true;
        Assert.True(platform.ShowSync);

        platform.IsConnected = false;
        Assert.False(platform.ShowSync);
    }

    [AvaloniaFact]
    public void Xbox_keeps_its_sidebar_line_whether_connected_or_not()
    {
        var vm = SampleData.Empty(TempFolder);
        var xbox = vm.Accounts.XboxSync;

        xbox.IsConnected = true;
        xbox.IsConnected = false;

        Assert.True(xbox.AlwaysShown);
        Assert.True(xbox.ShowSync);
    }

    [AvaloniaFact]
    public void The_window_is_told_when_an_account_is_connected_or_dropped()
    {
        var vm = SampleData.Empty(TempFolder);
        int told = 0;
        vm.Accounts.AccountChanged += () => told++;

        vm.Accounts.SteamSync.IsConnected = true;
        vm.Accounts.SteamSync.IsConnected = false;

        Assert.Equal(2, told);
    }

    [AvaloniaFact]
    public async Task The_common_sync_only_takes_the_connected_platforms()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        // Connected without any saved session: its fetch fails, which proves it was tried.
        vm.Accounts.SteamSync.IsConnected = true;

        await vm.Accounts.RefreshHistoryCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Accounts.SteamSync.Error);
        Assert.Null(vm.Accounts.XboxSync.Error);
        Assert.Null(vm.Accounts.PlayStationSync.Error);
    }
}
