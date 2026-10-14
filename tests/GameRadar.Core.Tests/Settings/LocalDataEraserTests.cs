using GameRadar.Core.Cache;
using GameRadar.Core.Logging;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

public class LocalDataEraserTests : IDisposable
{
    private readonly TempFolder _root = new();
    private readonly HttpClient _http = new();

    // Same layout as on a real computer: %LOCALAPPDATA%\GameRadar holds the
    // settings and secrets, and the cache\, images\ and logs\ subfolders.
    private string Local => Path.Combine(_root.Path, "Local", "GameRadar");

    public void Dispose()
    {
        _http.Dispose();
        _root.Dispose();
    }

    private LocalDataEraser Eraser(out SettingsStore settings, out CacheStore cache, out SecretStore secrets)
    {
        settings = new SettingsStore(Local);
        cache = new CacheStore(Path.Combine(Local, "cache"));
        secrets = new SecretStore(Local);
        var images = new ImageFileCache(_http, Path.Combine(Local, "images"));
        var log = new ErrorLog(Path.Combine(Local, "logs"), "1.0.0");
        return new LocalDataEraser(settings, cache, secrets, images, log, new DataFolder(Local));
    }

    private async Task FillAsync(SettingsStore settings, CacheStore cache, SecretStore secrets, string? root = null)
    {
        root ??= Local;
        await settings.SaveAsync(new AppSettings());
        await cache.SaveAsync("steam-library", "{}");
        await cache.SaveAsync("gamepass-catalog-fr-v5", "{}");
        secrets.Save(Secret.XboxAccount, new SavedXboxAccount("session", null), XboxJsonContext.Default.SavedXboxAccount);
        secrets.Save(Secret.SteamAccount, new SavedSteamAccount("key", "76561198000000000", null), SteamJsonContext.Default.SavedSteamAccount);
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllText(Path.Combine(root, "images", "0A1B2C.img"), "picture");
        // Leftover of a download interrupted by a crash.
        File.WriteAllText(Path.Combine(root, "images", "0A1B2C.3f2e.tmp"), "half a picture");
        new ErrorLog(Path.Combine(root, "logs"), "1.0.0").Write("Test", "une erreur");
    }

    [Fact]
    public async Task EraseAll_LeavesNoGameRadarFolderBehind()
    {
        var eraser = Eraser(out var settings, out var cache, out var secrets);
        await FillAsync(settings, cache, secrets);

        eraser.EraseAll();

        Assert.False(Directory.Exists(Local));
    }

    [Fact]
    public async Task EraseAll_ErasesTheSecretsEvenWhenAFileIsLocked()
    {
        var eraser = Eraser(out var settings, out var cache, out var secrets);
        await FillAsync(settings, cache, secrets);

        // A cache file opened by another program (antivirus, explorer...).
        using (File.Open(Path.Combine(Local, "cache", "steam-library.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(eraser.EraseAll);

        // The failure is reported, but the most sensitive data is gone, and
        // every other store was erased too.
        Assert.Empty(Directory.GetFiles(Local, "*.bin"));
        Assert.False(File.Exists(Path.Combine(Local, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(Local, "images")));
    }

    [Fact]
    public async Task EraseAll_InPortableMode_EmptiesTheDataFolderButKeepsIt()
    {
        // A "Data" folder next to the exe is the user's choice of the portable
        // mode: deleting it would silently bring back the normal mode.
        string exeFolder = Path.Combine(_root.Path, "exe");
        Directory.CreateDirectory(Path.Combine(exeFolder, DataFolder.PortableFolderName));
        var (data, _) = DataFolder.Resolve(exeFolder, Local);
        var settings = new SettingsStore(data.Root);
        var cache = new CacheStore(data.Cache);
        var secrets = new SecretStore(data.Root);
        var eraser = new LocalDataEraser(settings, cache, secrets,
            new ImageFileCache(_http, data.Images), new ErrorLog(data.Logs, "1.0.0"), data);
        await FillAsync(settings, cache, secrets, data.Root);

        eraser.EraseAll();

        Assert.True(Directory.Exists(data.Root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(data.Root));
    }

    [Fact]
    public async Task EraseAll_InNormalMode_StillRemovesTheFolder()
    {
        var eraser = Eraser(out var settings, out var cache, out var secrets);
        await FillAsync(settings, cache, secrets);

        eraser.EraseAll();

        Assert.False(Directory.Exists(Local));
    }

    [Fact]
    public void Deleting_a_secret_without_its_folder_does_not_throw()
    {
        // Signing out on a computer where no secret was ever saved.
        var secrets = new SecretStore(Path.Combine(Local, "never-created"));

        secrets.Delete(Secret.SteamAccount);
    }

    [Fact]
    public async Task EraseAll_KeepsFilesItDoesNotKnow()
    {
        // Someone who saved GameRadar.exe in its own data folder must not lose it.
        var eraser = Eraser(out var settings, out var cache, out var secrets);
        await FillAsync(settings, cache, secrets);
        string foreign = Path.Combine(Local, "GameRadar.exe");
        File.WriteAllText(foreign, "not ours");

        eraser.EraseAll();

        Assert.True(File.Exists(foreign));
        Assert.Single(Directory.EnumerateFileSystemEntries(Local));
    }

    [Fact]
    public async Task EraseAll_GivesAFreshStartToTheStores()
    {
        var eraser = Eraser(out var settings, out var cache, out var secrets);
        await FillAsync(settings, cache, secrets);

        eraser.EraseAll();

        Assert.Null(cache.TryLoad("steam-library"));
        Assert.Null(secrets.Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount));
        Assert.Empty(settings.Load().MarkedCompleted);
    }

    [Fact]
    public void EraseAll_DoesNothingWhenThereIsNothing()
    {
        var eraser = Eraser(out _, out _, out _);

        eraser.EraseAll();

        Assert.False(Directory.Exists(Local));
    }
}
