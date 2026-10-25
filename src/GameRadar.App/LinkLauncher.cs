using Avalonia.Platform.Storage;
using GameRadar.App.Resources;
using GameRadar.App.ViewModels;

namespace GameRadar.App;

/// <summary>
/// Opens what the view model asks for (its "openBrowser"): a web page in the
/// browser, a folder in the file explorer (the error log), a file in its
/// default app (the legal texts, in the text editor).
/// </summary>
public static class LinkLauncher
{
    public static Task<bool> OpenAsync(ILauncher launcher, Uri uri)
    {
        if (!uri.IsFile)
            return launcher.LaunchUriAsync(uri);

        // Folder and file must be told apart: a file launched as a folder
        // opens nothing, without any error.
        return Directory.Exists(uri.LocalPath)
            ? launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(uri.LocalPath))
            : launcher.LaunchFileInfoAsync(new FileInfo(uri.LocalPath));
    }

    /// <summary>
    /// Opens <paramref name="uri"/>; when nothing opens it (no default browser, a
    /// policy that blocks it), says so in the banner and the log. Never throws.
    /// </summary>
    public static async Task OpenOrReportAsync(ILauncher launcher, Uri uri, NoticeCenter notices)
    {
        Exception? error = null;
        try
        {
            if (await OpenAsync(launcher, uri))
                return;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        notices.Show(Strings.Error_LinkNotOpened);
        // The address in the log: which one failed (secrets masked by the log).
        notices.Log("Link", $"Could not open {uri}", error);
    }
}
