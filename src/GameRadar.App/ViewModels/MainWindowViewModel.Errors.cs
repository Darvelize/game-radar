using CommunityToolkit.Mvvm.Input;

namespace GameRadar.App.ViewModels;

// The error log part of the view model: errors nobody caught, and the
// button that shows the log files to send when something goes wrong.
// The banner and the log writes are in NoticeCenter.
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// An error no code path expected (see the global handlers in App): it
    /// is written to the log and the user is told where to find it.
    /// </summary>
    public void ReportUnexpectedError(string context, Exception ex) => Notices.ReportUnexpected(context, ex);

    [RelayCommand]
    private void OpenLogsFolder()
    {
        // Created if no error was ever written: the button must open something.
        Directory.CreateDirectory(_log.Folder);
        // A file URI: the app's launcher opens it in the Windows file explorer.
        _openBrowser(new Uri(_log.Folder + Path.DirectorySeparatorChar));
    }
}
