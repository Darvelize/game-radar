using System.Text.Json;
using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

public sealed class PrivacyTests : AppTest
{
    [AvaloniaFact]
    public void Erasing_asks_for_a_confirmation_first()
    {
        var vm = SampleData.Empty(TempFolder);

        vm.Privacy.AskEraseAllDataCommand.Execute(null);
        Assert.True(vm.Privacy.IsConfirmingErase);

        vm.Privacy.CancelEraseAllDataCommand.Execute(null);
        Assert.False(vm.Privacy.IsConfirmingErase);
    }

    [AvaloniaFact]
    public async Task Erasing_deletes_the_data_then_quits()
    {
        bool quit = false;
        var vm = SampleData.Empty(TempFolder, quit: () => quit = true);
        var saved = new SettingsStore(TempFolder).Load();
        saved.MarkedCompleted.Add("Xbox:123");
        await new SettingsStore(TempFolder).SaveAsync(saved);

        vm.Privacy.AskEraseAllDataCommand.Execute(null);
        vm.Privacy.EraseAllDataCommand.Execute(null);

        Assert.True(quit);
        Assert.False(Directory.Exists(TempFolder));
    }

    [AvaloniaFact]
    public async Task Erasing_waits_for_a_choice_still_being_saved()
    {
        // A box just unticked: its setting is written in the background. Erasing
        // now would let that save write settings.json back right after.
        bool quit = false;
        var vm = SampleData.Empty(TempFolder, quit: () => quit = true);
        vm.SourceFilter.ShowSteam = !vm.SourceFilter.ShowSteam;

        vm.Privacy.AskEraseAllDataCommand.Execute(null);
        vm.Privacy.EraseAllDataCommand.Execute(null);

        Assert.False(quit);
        Assert.StartsWith("Une synchronisation est en cours", vm.Notices.Message);

        // Once written, erasing goes through.
        await vm.WhenSettingsSavedAsync();
        vm.Privacy.EraseAllDataCommand.Execute(null);
        Assert.True(quit);
    }

    [AvaloniaTheory]
    [InlineData("microsoft", "https://account.live.com/consent/Manage")]
    [InlineData("steam", "https://steamcommunity.com/dev/apikey")]
    public void Revoking_opens_the_account_page(string service, string expected)
    {
        Uri? opened = null;
        var vm = SampleData.Empty(TempFolder, uri => opened = uri);

        if (service == "microsoft")
            vm.Privacy.OpenMicrosoftConsentsCommand.Execute(null);
        else
            vm.Privacy.OpenSteamApiKeyPageCommand.Execute(null);

        Assert.Equal(new Uri(expected), opened);
    }

    [AvaloniaFact]
    public async Task Erasing_waits_for_a_catalog_download()
    {
        // An old Game Pass catalog is downloaded again at startup: the download is held.
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        var hades = new CatalogGame("9NBLGGH4R315", "Hades", null, null, ["Action"], OnPc: true, OnConsole: true, LeavingSoon: false);
        await cache.SaveAsync("gamepass-catalog-fr-v6",
            TestJson.Serialize(new GameCatalog([hades], DateTimeOffset.UtcNow.AddDays(-10))));
        var network = new FakeStores { HoldGamePass = true };
        bool quit = false;
        var vm = SampleData.Empty(TempFolder, quit: () => quit = true, network: network);
        var starting = vm.InitializeAsync();

        await AssertErasingWaitsAsync(vm, () => quit, network.GamePassReached.Task,
            isBusy: () => vm.Catalogs.IsRefreshing, release: network.Release, pending: starting);
    }

    [AvaloniaFact]
    public async Task Erasing_waits_for_a_connection_being_checked()
    {
        // The Steam key is being checked (the request is held): the account is not saved yet.
        var network = new HoldingNetwork("api.steampowered.com");
        bool quit = false;
        var vm = SampleData.Empty(TempFolder, quit: () => quit = true, network: network);
        vm.Accounts.SteamKeyInput = "0123456789ABCDEF0123456789ABCDEF";
        vm.Accounts.SteamProfileInput = "76561198000000000";
        var connecting = vm.Accounts.ConnectSteamCommand.ExecuteAsync(null);

        // The check ends with Steam answering 404 here.
        await AssertErasingWaitsAsync(vm, () => quit, network.Reached.Task,
            isBusy: () => vm.Accounts.IsBusy, release: network.Release, pending: connecting);
    }

    /// <summary>
    /// An erase asked while something is running (<paramref name="pending"/>, held
    /// until <paramref name="release"/>) is refused with the "sync in progress"
    /// notice; once it is over, erasing goes through.
    /// </summary>
    private async Task AssertErasingWaitsAsync(
        MainWindowViewModel vm, Func<bool> hasQuit, Task reached, Func<bool> isBusy, Action release, Task pending)
    {
        try
        {
            await reached.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(isBusy());

            vm.Privacy.AskEraseAllDataCommand.Execute(null);
            vm.Privacy.EraseAllDataCommand.Execute(null);

            Assert.False(hasQuit());
            Assert.StartsWith("Une synchronisation est en cours", vm.Notices.Message);
        }
        finally
        {
            // Whatever happened above, what was held ends: nothing keeps
            // writing in the folder the test deletes afterwards.
            release();
        }

        await pending;
        await vm.WhenSettingsSavedAsync();
        vm.Privacy.EraseAllDataCommand.Execute(null);
        Assert.True(hasQuit());
    }
}
