using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;

namespace GameRadar.App.Tests;

public sealed class LinkLauncherTests : AppTest
{
    private readonly FakeLauncher _launcher = new();

    [AvaloniaFact]
    public async Task A_web_page_opens_in_the_browser()
    {
        await LinkLauncher.OpenAsync(_launcher, new Uri("https://store.steampowered.com/app/620"));

        Assert.Equal("https://store.steampowered.com/app/620", _launcher.Uri?.AbsoluteUri);
    }

    [AvaloniaFact]
    public async Task The_log_folder_opens_as_a_folder()
    {
        Directory.CreateDirectory(TempFolder);

        await LinkLauncher.OpenAsync(_launcher, new Uri(TempFolder + Path.DirectorySeparatorChar));

        Assert.IsAssignableFrom<IStorageFolder>(_launcher.Item);
    }

    [AvaloniaFact]
    public async Task A_file_opens_as_a_file()
    {
        // Launched as a folder, a text file opened nothing, silently.
        Directory.CreateDirectory(TempFolder);
        string path = Path.Combine(TempFolder, "notes.txt");
        File.WriteAllText(path, "GameRadar");

        await LinkLauncher.OpenAsync(_launcher, new Uri(path));

        var file = Assert.IsAssignableFrom<IStorageFile>(_launcher.Item);
        Assert.Equal("notes.txt", file.Name);
    }

    /// <summary>Records what it is asked to open instead of opening it.</summary>
    private sealed class FakeLauncher : ILauncher
    {
        public Uri? Uri { get; private set; }
        public IStorageItem? Item { get; private set; }

        public Task<bool> LaunchUriAsync(Uri uri)
        {
            Uri = uri;
            return Task.FromResult(true);
        }

        public Task<bool> LaunchFileAsync(IStorageItem storageItem)
        {
            Item = storageItem;
            return Task.FromResult(true);
        }
    }
}
