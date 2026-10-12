using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Settings;

namespace GameRadar.App.ViewModels;

// The privacy part of the app: where to revoke GameRadar's access, and
// "Effacer toutes mes données".
/// <param name="isBusy">
/// Background work that saves files is running (a sync, a settings save): the
/// player is asked to wait rather than erase in the middle of it. Only a
/// courtesy and a message: the stores refuse to write back once erased
/// (<c>EraseGuard</c>), whatever is still running.
/// </param>
/// <param name="stopBackgroundWork">Stops what could still write a file (Steam tags saved every 50 games, card pictures).</param>
public sealed partial class PrivacyViewModel(
    NoticeCenter notices,
    LocalDataEraser eraser,
    Action quit,
    Action<Uri> openBrowser,
    Func<bool> isBusy,
    Action stopBackgroundWork) : ObservableObject
{
    // Page where a Microsoft account lists the apps it gave access to, and
    // revokes them (required by the Microsoft APIs Terms of Use, section 5).
    private static readonly Uri MicrosoftConsentsPage = new("https://account.live.com/consent/Manage");

    // Page of the Steam Web API key, with its "Revoke" button.
    private static readonly Uri SteamApiKeyPage = new("https://steamcommunity.com/dev/apikey");

    // Sony's account management: Security › "Sign out of all devices"
    // kills every session, the NPSSO kept by GameRadar included.
    private static readonly Uri PlayStationAccountPage = new("https://www.playstation.com/acct/management");

    [RelayCommand]
    private void OpenMicrosoftConsents() => openBrowser(MicrosoftConsentsPage);

    [RelayCommand]
    private void OpenPlayStationAccountPage() => openBrowser(PlayStationAccountPage);

    [RelayCommand]
    private void OpenSteamApiKeyPage() => openBrowser(SteamApiKeyPage);

    /// <summary>True while the confirmation of "Effacer toutes mes données" is shown.</summary>
    [ObservableProperty]
    public partial bool IsConfirmingErase { get; set; }

    [RelayCommand]
    private void AskEraseAllData() => IsConfirmingErase = true;

    [RelayCommand]
    private void CancelEraseAllData() => IsConfirmingErase = false;

    [RelayCommand]
    private void EraseAllData()
    {
        IsConfirmingErase = false;
        if (isBusy())
        {
            notices.Show(Strings.Privacy_Erase_Busy);
            return;
        }

        // Stops what could still write a file, then erases, then quits: the
        // app has nothing left to show, and a restart gives a truly fresh start.
        stopBackgroundWork();
        try
        {
            eraser.EraseAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file opened by another program (antivirus, explorer...).
            notices.Show(Localization.Format(Strings.Privacy_Erase_Failed, ex.Message));
            return;
        }
        quit();
    }
}
