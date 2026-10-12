using Avalonia.Platform.Storage;

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
}
