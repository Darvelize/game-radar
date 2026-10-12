using GameRadar.Core.Cache;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Cache;

public class CacheStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void TryLoad_ReturnsNullWhenMissing()
    {
        Assert.Null(new CacheStore(_folder.Path).TryLoad("missing"));
    }

    [Fact]
    public async Task SaveAsync_ThenTryLoad_RoundTripsContentAndDate()
    {
        var store = new CacheStore(_folder.Path);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await store.SaveAsync("xbox-history", """{"a":1}""");
        var entry = store.TryLoad("xbox-history");

        Assert.NotNull(entry);
        Assert.Equal("""{"a":1}""", entry.Content);
        Assert.True(entry.SavedAt >= before);
        // The temp file used for the atomic write must not remain.
        Assert.Single(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousContent()
    {
        var store = new CacheStore(_folder.Path);

        await store.SaveAsync("key", "v1");
        await store.SaveAsync("key", "v2");

        Assert.Equal("v2", store.TryLoad("key")?.Content);
    }

    [Fact]
    public async Task SaveAsync_TwoSavesOfOneKeyAtOnceBothSucceed()
    {
        // A cancelled lookup and its newer replacement save the same cache
        // together: one temp file for both made the second fail.
        var store = new CacheStore(_folder.Path);
        string big = new('x', 4_000_000);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.SaveAsync("store-genres", big + i)));

        Assert.StartsWith(big, store.TryLoad("store-genres")!.Content);
        Assert.Single(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public async Task MergeDictionaryAsync_AddsToWhatIsSavedNow()
    {
        var store = new CacheStore(_folder.Path);
        await store.MergeDictionaryAsync("genres", new Dictionary<string, int> { ["a"] = 1, ["b"] = 1 }, TestJsonContext.Default.DictionaryStringInt32);

        // Another run saved meanwhile: its entries stay, the same ones are replaced.
        await store.MergeDictionaryAsync("genres", new Dictionary<string, int> { ["b"] = 2, ["c"] = 2 }, TestJsonContext.Default.DictionaryStringInt32);

        Assert.Equal(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 2 }, store.LoadDictionary("genres", TestJsonContext.Default.DictionaryStringInt32));
    }

    [Fact]
    public async Task SaveAsync_AfterEraseAll_WritesNothingBack()
    {
        var store = new CacheStore(_folder.Path);
        await store.SaveAsync("steam-tags", "v1");

        // A lookup cancelled by "Effacer toutes mes données" still saves what it found.
        store.EraseAll();
        await store.SaveAsync("steam-tags", "v2");

        Assert.Null(store.TryLoad("steam-tags"));
        Assert.False(Directory.Exists(_folder.Path));
    }

    [Theory]
    [InlineData("../settings")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Keys_ThatCouldEscapeTheFolderAreRejected(string key)
    {
        Assert.Throws<ArgumentException>(() => new CacheStore(_folder.Path).TryLoad(key));
    }
}
