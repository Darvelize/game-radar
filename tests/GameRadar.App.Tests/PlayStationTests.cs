using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace GameRadar.App.Tests;

/// <summary>
/// The PlayStation account in the app: its history among the others, its
/// badge, its line in the sidebar. No network: Sony and the catalogs answer 404.
/// </summary>
public sealed class PlayStationTests : AppTest
{
    [AvaloniaFact]
    public async Task The_cached_PlayStation_history_shows_with_its_badge_even_signed_out()
    {
        // The raw pages of both lists, as PlayStationHistorySource caches them.
        Directory.CreateDirectory(Path.Combine(TempFolder, "cache"));
        await File.WriteAllTextAsync(Path.Combine(TempFolder, "cache", "playstation-history.json"), """
            { "trophyTitles": [ { "trophyTitles": [
                { "npCommunicationId": "NPWR00001_00", "trophyTitleName": "Space Heroes", "progress": 62,
                  "earnedTrophies": { "bronze": 20, "silver": 5, "gold": 1, "platinum": 1 },
                  "definedTrophies": { "bronze": 30, "silver": 10, "gold": 5, "platinum": 1 },
                  "lastUpdatedDateTime": "2026-08-01T20:00:00Z" } ], "totalItemCount": 1 } ],
              "games": [ { "titles": [], "totalItemCount": 0 } ] }
            """);
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());

        await vm.InitializeAsync();

        var row = vm.History.Games.Single(r => r.Name == "Space Heroes");
        Assert.True(row.IsPlayStation);
        // Platinum, DLC trophies missing (62 %): finished, not mastered; no flag to tick.
        Assert.False(row.IsMastered);
        Assert.Equal("Terminé", row.StatusLabel);
        Assert.False(row.CanMarkCompleted);
        // The platform says so: a ticked flag, shown, that the player cannot untick.
        Assert.True(row.IsCompletedOnPlatform);
        // A history, so no welcome screen; the sidebar shows its age.
        Assert.True(vm.History.HasHistory);
        Assert.True(vm.Accounts.PlayStationSync.ShowSync);
        // Signed out (no NPSSO saved): the sync button does not count on it.
        Assert.False(vm.Accounts.PlayStationSync.IsConnected);
    }

    [AvaloniaFact]
    public async Task A_PlayStation_game_without_its_platinum_can_be_marked_finished()
    {
        // A game of the played list (PS5, play time) with its trophy set, no platinum.
        Directory.CreateDirectory(Path.Combine(TempFolder, "cache"));
        await File.WriteAllTextAsync(Path.Combine(TempFolder, "cache", "playstation-history.json"), """
            { "trophyTitles": [ { "trophyTitles": [
                { "npCommunicationId": "NPWR00002_00", "trophyTitleName": "Story Quest", "progress": 40,
                  "earnedTrophies": { "bronze": 10, "silver": 2, "gold": 0, "platinum": 0 },
                  "definedTrophies": { "bronze": 30, "silver": 10, "gold": 5, "platinum": 1 },
                  "lastUpdatedDateTime": "2026-08-01T20:00:00Z" } ], "totalItemCount": 1 } ],
              "games": [ { "titles": [
                { "titleId": "PPSA00002_00", "name": "Story Quest", "localizedName": "Story Quest", "category": "ps5_native_game",
                  "playDuration": "PT20H5M", "lastPlayedDateTime": "2026-08-01T20:00:00Z" } ], "totalItemCount": 1 } ] }
            """);
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        await vm.InitializeAsync();

        var row = vm.History.Games.Single(r => r.Name == "Story Quest");
        Assert.True(row.CanMarkCompleted);

        row.IsCompleted = true;
        await vm.WhenSettingsSavedAsync();
        Dispatcher.UIThread.RunJobs();

        // Rebuilt with the mark: still the player's flag, which can be unticked.
        var marked = vm.History.Games.Single(r => r.Name == "Story Quest");
        Assert.True(marked.IsCompleted);
        Assert.True(marked.CanMarkCompleted);
    }

    [AvaloniaFact]
    public async Task A_refused_token_is_explained_and_cleared_from_the_screen()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        vm.Accounts.NpssoInput = "not a token";

        await vm.Accounts.ConnectPlayStationCommand.ExecuteAsync(null);

        // Refused before any request: it is not even shaped like an NPSSO.
        Assert.False(vm.Accounts.PlayStationSync.IsConnected);
        Assert.StartsWith("Colle ton jeton NPSSO", vm.Notices.Message);
        Assert.Equal("", vm.Accounts.NpssoInput);
    }
}
