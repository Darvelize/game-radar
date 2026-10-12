using GameRadar.Core.Cache;
using GameRadar.Core.Logging;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

public class EraseGuardTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void NewGuard_IsNotErased_AndTripSticks()
    {
        var guard = new EraseGuard();

        Assert.False(guard.IsErased);
        guard.Trip();
        Assert.True(guard.IsErased);
    }

    [Fact]
    public async Task WriteAtomicallyAsync_WritesNothingOnceTheGuardIsTripped()
    {
        var guard = new EraseGuard();
        string path = Path.Combine(_folder.Path, "file.json");
        guard.Trip();

        bool written = await LocalFiles.WriteAtomicallyAsync(
            path, (file, token) => file.WriteAsync(new byte[] { 1 }, token).AsTask(), guard, CancellationToken.None);

        Assert.False(written);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task StoresSharingAGuard_AreAllBlockedByOneErase()
    {
        var guard = new EraseGuard();
        var first = new CacheStore(Path.Combine(_folder.Path, "a"), guard);
        var second = new CacheStore(Path.Combine(_folder.Path, "b"), guard);

        first.EraseAll();
        await second.SaveAsync("key", "late write");

        Assert.Null(second.TryLoad("key"));
    }

    [Fact]
    public async Task SettingsStore_WritesNothingAfterItsErase_AndDoesNotThrow()
    {
        var store = new SettingsStore(_folder.Path);

        store.EraseAll();
        await store.SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });

        Assert.False(store.Exists);
        Assert.False(Directory.Exists(_folder.Path) && Directory.EnumerateFileSystemEntries(_folder.Path).Any());
    }

    [Fact]
    public void SecretStore_WritesNothingAfterItsErase_AndDoesNotThrow()
    {
        var secrets = new SecretStore(_folder.Path);

        secrets.EraseAll();
        secrets.Save(Secret.SteamAccount, new SavedSteamAccount("key", "76561198000000000", null), SteamJsonContext.Default.SavedSteamAccount);

        Assert.False(Directory.Exists(_folder.Path) && Directory.EnumerateFileSystemEntries(_folder.Path).Any());
    }

    [Fact]
    public async Task SettingsStore_AndSecretStore_ShareTheGuardOfTheCache()
    {
        var guard = new EraseGuard();
        var settings = new SettingsStore(_folder.Path, guard);
        var secrets = new SecretStore(_folder.Path, guard);

        new CacheStore(Path.Combine(_folder.Path, "cache"), guard).EraseAll();
        await settings.SaveAsync(new AppSettings());
        secrets.Save(Secret.SteamAccount, new SavedSteamAccount("key", "76561198000000000", null), SteamJsonContext.Default.SavedSteamAccount);

        Assert.False(settings.Exists);
        Assert.False(Directory.Exists(_folder.Path) && Directory.EnumerateFileSystemEntries(_folder.Path).Any());
    }

    [Fact]
    public async Task SettingsStore_WithAFreshGuard_StillSaves()
    {
        var store = new SettingsStore(_folder.Path, new EraseGuard());

        await store.SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });

        Assert.Equal(PsPlusTier.Extra, store.Load().PsPlusPlan);
    }

    [Fact]
    public void ErrorLog_WritesNothingOnceTheSharedGuardIsTripped()
    {
        var guard = new EraseGuard();
        var log = new ErrorLog(Path.Combine(_folder.Path, "logs"), "1.0.0", guard: guard);
        guard.Trip();

        log.Write("Test", "a background task failed");

        Assert.False(Directory.Exists(Path.Combine(_folder.Path, "logs")));
    }

    [Fact]
    public async Task OneEraseAll_StopsEveryStoreOfTheApp()
    {
        var data = new DataFolder(Path.Combine(_folder.Path, "GameRadar"));
        var settings = new SettingsStore(data.Root, data.Guard);
        var secrets = new SecretStore(data.Root, data.Guard);
        var cache = new CacheStore(data.Cache, data.Guard);
        var images = new ImageFileCache(new HttpClient(), data.Images, guard: data.Guard);
        var log = new ErrorLog(data.Logs, "1.0.0", guard: data.Guard);
        var eraser = new LocalDataEraser(settings, cache, secrets, images, log, data);

        eraser.EraseAll();
        await settings.SaveAsync(new AppSettings());
        secrets.Save(Secret.SteamAccount, new SavedSteamAccount("key", "76561198000000000", null), SteamJsonContext.Default.SavedSteamAccount);
        await cache.SaveAsync("key", "late");
        log.Write("Test", "late error");

        Assert.False(Directory.Exists(data.Root));
    }

    [Fact]
    public async Task AFailedErase_LeavesTheAppAbleToSaveItsChoices()
    {
        // The app stays open after "Certaines données n'ont pas pu être
        // effacées": a later choice or sign-in must still be saved.
        var data = new DataFolder(Path.Combine(_folder.Path, "GameRadar"));
        var settings = new SettingsStore(data.Root, data.Guard);
        var secrets = new SecretStore(data.Root, data.Guard);
        var cache = new CacheStore(data.Cache, data.Guard);
        var eraser = new LocalDataEraser(settings, cache, secrets,
            new ImageFileCache(new HttpClient(), data.Images, guard: data.Guard), new ErrorLog(data.Logs, "1.0.0", guard: data.Guard), data);
        await cache.SaveAsync("steam-library", "{}");

        using (File.Open(Path.Combine(data.Cache, "steam-library.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(eraser.EraseAll);
        await settings.SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });

        Assert.Equal(PsPlusTier.Extra, settings.Load().PsPlusPlan);
    }

    [Fact]
    public async Task StoresWithTheirOwnGuard_AreIndependent()
    {
        var first = new CacheStore(Path.Combine(_folder.Path, "a"));
        var second = new CacheStore(Path.Combine(_folder.Path, "b"));

        first.EraseAll();
        await second.SaveAsync("key", "still saved");

        Assert.NotNull(second.TryLoad("key"));
    }
}
