using GameRadar.Core.Cache;
using GameRadar.Core.GOG;
using GameRadar.Core.GamePass;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Cache;

public class CacheCleanupTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private CacheStore StoreWith(params string[] keys)
    {
        var store = new CacheStore(_folder.Path);
        foreach (var key in keys)
            store.SaveAsync(key, "{}").GetAwaiter().GetResult();
        return store;
    }

    private string[] Remaining() =>
        Directory.GetFiles(_folder.Path).Select(f => Path.GetFileNameWithoutExtension(f)).Order().ToArray();

    [Fact]
    public void RemoveObsolete_DeletesAnOlderVersionOfAnActiveFamily()
    {
        var store = StoreWith("gamepass-catalog-en-v5", "gamepass-catalog-en-v6");

        CacheCleanup.RemoveObsolete(store, ["gamepass-catalog-en-v6"], []);

        Assert.Equal(["gamepass-catalog-en-v6"], Remaining());
    }

    [Fact]
    public void RemoveObsolete_DeletesANewerVersionToo()
    {
        // A downgrade leaves a file of the future: not read, so not kept.
        var store = StoreWith("steam-catalog-fr-v3", "steam-catalog-fr-v9");

        CacheCleanup.RemoveObsolete(store, ["steam-catalog-fr-v3"], []);

        Assert.Equal(["steam-catalog-fr-v3"], Remaining());
    }

    [Fact]
    public void RemoveObsolete_KeepsAProtectedKey()
    {
        var store = StoreWith("gamepass-catalog-fr-v5", "gamepass-catalog-fr-v6");

        CacheCleanup.RemoveObsolete(store, ["gamepass-catalog-fr-v6"], ["gamepass-catalog-fr-v5"]);

        Assert.Equal(["gamepass-catalog-fr-v5", "gamepass-catalog-fr-v6"], Remaining());
    }

    [Fact]
    public void RemoveObsolete_KeepsAnUnknownFamilyAndAKeyWithoutVersion()
    {
        var store = StoreWith("xbox-history", "steam-library", "unknown-thing-v1", "steam-catalog-fr-v2");

        CacheCleanup.RemoveObsolete(store, ["steam-catalog-fr-v3"], []);

        // Only steam-catalog-fr-v2 shares a family with an active key.
        Assert.Equal(["steam-library", "unknown-thing-v1", "xbox-history"], Remaining());
    }

    [Fact]
    public void RemoveObsolete_KeepsAnotherLanguageOfTheSameCatalog()
    {
        var store = StoreWith("gamepass-catalog-en-v6", "gamepass-catalog-fr-v6");

        CacheCleanup.RemoveObsolete(store, ["gamepass-catalog-fr-v6"], []);

        Assert.Equal(["gamepass-catalog-en-v6", "gamepass-catalog-fr-v6"], Remaining());
    }

    [Fact]
    public void RemoveObsolete_LeavesTempFilesAlone()
    {
        var store = StoreWith("steam-catalog-fr-v2");
        File.WriteAllText(Path.Combine(_folder.Path, "steam-catalog-fr-v2.json.1234.tmp"), "{}");

        CacheCleanup.RemoveObsolete(store, ["steam-catalog-fr-v3"], []);

        Assert.Equal(["steam-catalog-fr-v2.json.1234"], Remaining());
    }

    [Fact]
    public void RemoveObsolete_NeverThrowsOnALockedFile()
    {
        var store = StoreWith("steam-catalog-fr-v2");
        using var locked = new FileStream(Path.Combine(_folder.Path, "steam-catalog-fr-v2.json"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var error = Record.Exception(() => CacheCleanup.RemoveObsolete(store, ["steam-catalog-fr-v3"], []));

        Assert.Null(error);
    }

    [Fact]
    public async Task RemoveObsolete_ForTheApp_KeepsEveryActiveCacheAndTheMigrationOnes()
    {
        var store = new CacheStore(_folder.Path);
        // The caches the app reads today (each source says its own key)...
        string[] active =
        [
            new GamePassCatalogSource(store).CacheKey, new StoreGenreSource(store).CacheKey,
            new SteamGenreSource(store).CacheKey, new PsPlusCatalogSource(store).CacheKey,
            SteamCatalogSource.CacheKey, SteamTagSource.CacheKey, GogCatalogSource.CacheKey,
        ];
        // ...the two still read by the English titles migration, and one of each family of a former version.
        string[] kept = [.. active, EnglishTitlesMigration.FrenchGamePassKey, EnglishTitlesMigration.FrenchSteamKey, "xbox-history"];
        string[] obsolete = ["gamepass-catalog-fr-v1", "store-genres-fr-v1", "steam-genres-fr-v0", "psplus-catalog-fr-fr-v0", "steam-catalog-fr-v1", "steam-tags-v0", "gog-catalog-fr-v0"];
        foreach (var key in kept.Concat(obsolete))
            await store.SaveAsync(key, "{}");

        CacheCleanup.RemoveObsolete(store);

        Assert.Equal(kept.Order(), Remaining());
    }

    [Fact]
    public void RemoveObsolete_DoesNothingWhenTheFolderIsMissing()
    {
        var store = new CacheStore(Path.Combine(_folder.Path, "nope"));

        var error = Record.Exception(() => CacheCleanup.RemoveObsolete(store, ["steam-catalog-fr-v3"], []));

        Assert.Null(error);
    }
}
