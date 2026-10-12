using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Models;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One synced platform (Xbox, Steam, PlayStation) as the app sees it: its
/// history, its status line in Settings › Data, its line in the sidebar.
/// What differs from one platform to the next is given when it is created
/// (MainWindowViewModel), so a new platform is one more entry in a list.
/// </summary>
public sealed partial class SyncedPlatform : ObservableObject
{
    /// <summary>
    /// The <see cref="PlayedGame.Platform"/> of its games (its connector's
    /// PlatformName: "Xbox", "Steam"...), also its name on screen: a brand
    /// name, never translated.
    /// </summary>
    public required string Platform { get; init; }

    /// <summary>"Historique Xbox": its line in Settings › Data, the sidebar tooltip.</summary>
    public required string Title { get; init; }

    /// <summary>Context of its errors in the log, in fixed English.</summary>
    public required string LogContext { get; init; }

    /// <summary>Its sidebar line even without history nor account (Xbox, the first platform).</summary>
    public bool AlwaysShown { get; init; }

    public required string FetchingStatus { get; init; }

    public required string NoHistoryStatus { get; init; }

    /// <summary>The cached history (synchronous file read: run on a background thread).</summary>
    public required Func<GameHistory?> LoadCached { get; init; }

    /// <summary>Fetches the history; the platform is given to report progress on its status line.</summary>
    public required Func<SyncedPlatform, Task<GameHistory>> Fetch { get; init; }

    /// <summary>The message of a failed fetch, from the most specific error to the most generic.</summary>
    public required Func<Exception, string> ErrorMessage { get; init; }

    /// <summary>Called after each fetch, failed or not: the account may have changed (renewed, dropped).</summary>
    public Action? AfterFetch { get; init; }

    /// <summary>Its status line once it has a history.</summary>
    public required Func<GameHistory, string> Describe { get; init; }

    /// <summary>
    /// Signed in / connected: synced by the common button, its sidebar line
    /// shown. Set by the account part (AccountsViewModel.UpdateXAccount); the
    /// view model listens to it once for all the platforms.
    /// </summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    // Its sidebar line follows at once, not at the next refresh of the relative times.
    partial void OnIsConnectedChanged(bool value) => ShowSync = ShouldShowSync();

    public GameHistory? History { get; set; }

    public IReadOnlyList<PlayedGame> Games => History?.Games ?? [];

    /// <summary>Why the last fetch failed; kept on the status line until the next fetch.</summary>
    public string? Error { get; set; }

    /// <summary>Its history state, or the current operation (fetching, signing in...).</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>The age of its history in the sidebar ("il y a 2 h").</summary>
    [ObservableProperty]
    public partial string SyncLabel { get; set; } = Strings.Sync_None;

    /// <summary>Its sidebar line, only for the players of this platform.</summary>
    [ObservableProperty]
    public partial bool ShowSync { get; set; }

    public void UpdateStatus() =>
        Status = Error ?? (History is null ? NoHistoryStatus : Describe(History));

    public void RefreshRelativeTime(DateTimeOffset now)
    {
        SyncLabel = History is null ? Strings.Sync_None : RelativeTime.Format(History.FetchedAt, now);
        ShowSync = ShouldShowSync();
    }

    private bool ShouldShowSync() => AlwaysShown || History is not null || IsConnected;
}
