using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Cache;

public class CompactCatalogCacheTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsTheGamesAndTheDate()
    {
        var cache = new CompactCatalogCache(new CacheStore(_folder.Path), "test-catalog-v1");
        Assert.Null(cache.Load());

        var saved = await cache.SaveAsync([Catalog("1", "Hades")], Empty);

        var loaded = cache.Load()!;
        Assert.Equal("Hades", Assert.Single(loaded.Games).Title);
        Assert.Equal(saved.FetchedAt, loaded.FetchedAt);
    }

    [Fact]
    public async Task Load_ACorruptedCacheCountsAsMissing()
    {
        var store = new CacheStore(_folder.Path);
        await store.SaveAsync("test-catalog-v1", "{ not json");

        Assert.Null(new CompactCatalogCache(store, "test-catalog-v1").Load());
    }

    [Fact]
    public void IsStale_AfterAWeek()
    {
        var fetched = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var catalog = new GameCatalog([], fetched);

        Assert.False(CompactCatalogCache.IsStale(catalog, fetched.AddDays(6)));
        Assert.True(CompactCatalogCache.IsStale(catalog, fetched.AddDays(8)));
    }
    [Fact]
    public async Task SaveAsync_AnEmptyCatalog_ThrowsTheGivenErrorAndKeepsTheOldOne()
    {
        var cache = new CompactCatalogCache(new CacheStore(_folder.Path), "test-catalog-v1");
        await cache.SaveAsync([Catalog("1", "Hades")], Empty);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SaveAsync([], Empty));

        Assert.Equal("empty", error.Message);
        Assert.Equal("Hades", Assert.Single(cache.Load()!.Games).Title);
    }

    [Fact]
    public async Task SaveAsync_RefusedAgainstTheSavedCatalog_KeepsIt_ButEmptinessComesFirst()
    {
        var cache = new CompactCatalogCache(new CacheStore(_folder.Path), "test-catalog-v1");
        await cache.SaveAsync([Catalog("1", "Hades")], Empty);
        Func<GameCatalog, Exception?> lostHades = previous =>
            previous.Games.Any(g => g.Title == "Hades") ? new InvalidOperationException("lost") : null;

        var lost = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SaveAsync([Catalog("2", "Celeste")], Empty, lostHades));
        var empty = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SaveAsync([], Empty, lostHades));

        Assert.Equal("lost", lost.Message);
        Assert.Equal("empty", empty.Message);
        Assert.Equal("Hades", Assert.Single(cache.Load()!.Games).Title);
    }

    private static Exception Empty() => new InvalidOperationException("empty");
}
