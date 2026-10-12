using Avalonia.Headless.XUnit;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

public sealed class LibraryStateTests : AppTest
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 9, 12, 0, TimeSpan.Zero);

    [AvaloniaFact]
    public async Task The_combined_history_is_the_platform_games_then_the_games_added_by_hand()
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.Add(new ManualGame("Hollow Knight", ManualPlatform.Switch, ManualLevel.Completed));
        await store.SaveAsync(settings, TestContext.Current.CancellationToken);
        var vm = SampleData.Empty(TempFolder);
        vm.Accounts.SteamSync.History = new GameHistory(
            [new PlayedGame("Steam", "230410", "Warframe", true, 212, 552, 38, Now, PlayStatus.Played, PlaytimeMinutes: 6000)], Now);

        var combined = vm.Library.CombinedHistory();

        Assert.Equal(["Warframe", "Hollow Knight"], combined.Select(g => g.Name));
    }

    [AvaloniaFact]
    public void Without_any_history_the_combined_history_is_empty()
    {
        var vm = SampleData.Empty(TempFolder);

        Assert.Empty(vm.Library.CombinedHistory());
    }
}
