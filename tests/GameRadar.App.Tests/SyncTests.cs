using Avalonia.Headless.XUnit;

namespace GameRadar.App.Tests;

/// <summary>
/// The sync of the histories: every connected platform at once, each with
/// its own outcome on its own status line. No network.
/// </summary>
public sealed class SyncTests : AppTest
{
    [AvaloniaFact]
    public async Task Each_failed_platform_keeps_its_own_error_after_the_lists_are_rebuilt()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        // Marked connected without any saved session: both fetches fail, together.
        vm.Accounts.XboxSync.IsConnected = true;
        vm.Accounts.SteamSync.IsConnected = true;

        await vm.Accounts.RefreshHistoryCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Accounts.XboxSync.Error);
        Assert.NotNull(vm.Accounts.SteamSync.Error);
        Assert.Equal(vm.Accounts.XboxSync.Error, vm.Accounts.XboxSync.Status);
        Assert.Equal(vm.Accounts.SteamSync.Error, vm.Accounts.SteamSync.Status);
        // Not connected: not synced.
        Assert.Null(vm.Accounts.PlayStationSync.Error);
        Assert.Contains(vm.Notices.Message, new[] { vm.Accounts.XboxSync.Error, vm.Accounts.SteamSync.Error });

        // The status lines are rebuilt with the lists (another platform synced,
        // a game added by hand...): the failure stays until the next sync.
        foreach (var platform in vm.Accounts.SyncedPlatforms)
            platform.UpdateStatus();

        Assert.Equal(vm.Accounts.XboxSync.Error, vm.Accounts.XboxSync.Status);
        Assert.Equal(vm.Accounts.SteamSync.Error, vm.Accounts.SteamSync.Status);
    }

    [AvaloniaFact]
    public async Task A_platform_being_synced_still_says_so_when_a_catalog_rebuilds_the_lists()
    {
        var network = new HoldingNetwork("www.gog.com");
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.Accounts.GogProfileInput = "Radar_Player";
        var connecting = vm.Accounts.ConnectGogCommand.ExecuteAsync(null);
        await network.Reached.Task;

        // A catalog arrived meanwhile: the lists and the status lines are rebuilt.
        vm.History.Rebuild();

        Assert.Equal(vm.Accounts.GogSync.FetchingStatus, vm.Accounts.GogSync.Status);
        network.Release();
        await connecting;
    }
}
