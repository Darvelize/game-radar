using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using GameRadar.App.Resources;
using GameRadar.App.ViewModels;
using GameRadar.Core.Logging;

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

    [AvaloniaFact]
    public async Task A_link_nothing_opens_is_said_in_the_banner_and_the_log()
    {
        // No default browser, or a policy that blocks it: the click must not do nothing silently.
        var notices = new NoticeCenter(new ErrorLog(Path.Combine(TempFolder, "logs"), "test"));

        await LinkLauncher.OpenOrReportAsync(new FakeLauncher { Opens = false }, new Uri("https://store.steampowered.com/app/620"), notices);

        Assert.Equal(Strings.Error_LinkNotOpened, notices.Message);
        Assert.Contains("https://store.steampowered.com/app/620",
            File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(TempFolder, "logs"), "*.log"))));
    }

    /// <summary>Records what it is asked to open instead of opening it.</summary>
    private sealed class FakeLauncher : ILauncher
    {
        public Uri? Uri { get; private set; }
        public IStorageItem? Item { get; private set; }
        public bool Opens { get; init; } = true;

        public Task<bool> LaunchUriAsync(Uri uri)
        {
            Uri = uri;
            return Task.FromResult(Opens);
        }

        public Task<bool> LaunchFileAsync(IStorageItem storageItem)
        {
            Item = storageItem;
            return Task.FromResult(true);
        }
    }
}
