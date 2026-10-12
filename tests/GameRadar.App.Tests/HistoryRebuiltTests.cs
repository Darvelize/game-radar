using Avalonia.Headless.XUnit;
using GameRadar.Core.Models;

namespace GameRadar.App.Tests;

/// <summary>
/// What follows a rebuild of the history rows: the window listens to
/// <c>History.Rebuilt</c> and refreshes the figures drawn from the rows.
/// </summary>
public sealed class HistoryRebuiltTests : AppTest
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 9, 12, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public void A_rebuild_tells_its_listeners_once()
    {
        var vm = SampleData.Empty(TempFolder);
        int heard = 0;
        vm.History.Rebuilt += () => heard++;

        vm.History.Rebuild();

        Assert.Equal(1, heard);
    }

    [AvaloniaFact]
    public void A_rebuild_refreshes_the_game_pass_line_of_the_catalog()
    {
        var vm = SampleData.Empty(TempFolder);
        Assert.Equal("", vm.Catalogs.GamePass.Status);
        vm.Catalogs.GamePass.Catalog = new GameCatalog(
            [new CatalogGame("9NBLGGH4R315", "Hades", null, null, ["Action"], OnPc: true, OnConsole: true, LeavingSoon: false)], Now);

        vm.History.Rebuild();

        Assert.NotEqual("", vm.Catalogs.GamePass.Status);
    }

    [AvaloniaFact]
    public void A_rebuild_refreshes_the_relative_times_of_the_catalog()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.Catalogs.GamePass.Catalog = new GameCatalog(
            [new CatalogGame("9NBLGGH4R315", "Hades", null, null, ["Action"], OnPc: true, OnConsole: true, LeavingSoon: false)], Now);
        string waiting = vm.CatalogSyncLabel;

        vm.History.Rebuild();

        Assert.NotEqual(waiting, vm.CatalogSyncLabel);
    }
}
