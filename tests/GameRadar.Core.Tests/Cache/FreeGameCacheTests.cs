using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Cache;

public class FreeGameCacheTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static FreeGame Game(string title, DateTimeOffset? until = null) =>
        new(title, $"https://store.epicgames.com/p/{title}", until);

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsTheGamesAndTheDate()
    {
        var cache = new FreeGameCache(new CacheStore(_folder.Path), FreeGameCache.EpicKey);
        Assert.Null(cache.Load());

        var saved = await cache.SaveAsync([new FreeGame("Hue", "https://gaming.amazon.com/hue-epic/dp/1", Now, "https://img/hue.jpg", "Epic Games")]);

        var loaded = cache.Load()!;
        Assert.Equal(new FreeGame("Hue", "https://gaming.amazon.com/hue-epic/dp/1", Now, "https://img/hue.jpg", "Epic Games"), Assert.Single(loaded.Games));
        Assert.Equal(saved.FetchedAt, loaded.FetchedAt);
    }

    [Fact]
    public async Task Load_AListSavedBeforePlayOnly_IsToKeep()
    {
        // Saved by 1.7.0 before the Steam section: no "PlayOnly" field.
        var store = new CacheStore(_folder.Path);
        await store.SaveAsync(FreeGameCache.EpicKey,
            """{"Games":[{"Title":"Hue","Link":"https://store.epicgames.com/p/hue","Until":"2026-09-24T15:00:00+00:00"}],"FetchedAt":"2026-09-23T12:00:00+00:00"}""");

        Assert.False(Assert.Single(new FreeGameCache(store, FreeGameCache.EpicKey).Load()!.Games).PlayOnly);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_KeepsPlayOnly()
    {
        var cache = new FreeGameCache(new CacheStore(_folder.Path), FreeGameCache.EpicKey);

        await cache.SaveAsync([new FreeGame("Battlefield 6", "https://store.steampowered.com/app/2807960/", Now, PlayOnly: true)]);

        Assert.True(Assert.Single(cache.Load()!.Games).PlayOnly);
    }

    [Fact]
    public async Task SaveAsync_KeepsAnEmptyList()
    {
        // A week without any gift is possible: nothing to show, not an error.
        var cache = new FreeGameCache(new CacheStore(_folder.Path), FreeGameCache.EpicKey);

        await cache.SaveAsync([]);

        Assert.Empty(cache.Load()!.Games);
    }

    [Fact]
    public async Task Load_ACorruptedCacheCountsAsMissing()
    {
        var store = new CacheStore(_folder.Path);
        await store.SaveAsync(FreeGameCache.EpicKey, "{ not json");

        Assert.Null(new FreeGameCache(store, FreeGameCache.EpicKey).Load());
    }

    [Fact]
    public void NeedsDownload_WithoutAList()
    {
        Assert.True(FreeGameCache.NeedsDownload(null, Now));
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(25, true)]
    public void NeedsDownload_AfterADay(int hoursAgo, bool needed)
    {
        var list = new FreeGameList([Game("A", Now.AddDays(3))], Now.AddHours(-hoursAgo));

        Assert.Equal(needed, FreeGameCache.NeedsDownload(list, Now));
    }

    [Fact]
    public void NeedsDownload_AsSoonAsAnOfferEnded()
    {
        // Epic changes its games on Thursday at 17:00: five minutes later, the new ones are asked.
        var list = new FreeGameList([Game("Ended", Now.AddMinutes(-5)), Game("Still free", Now.AddDays(3))], Now.AddMinutes(-10));

        Assert.True(FreeGameCache.NeedsDownload(list, Now));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(120, true)]
    public void NeedsDownload_ReadWithAnOfferAlreadyEnded_AtMostOnceAnHour(int minutesAgo, bool needed)
    {
        // Xbox's list stays online, ended, from Monday to Wednesday: not asked again at each visit.
        var list = new FreeGameList([Game("Ended", Now.AddDays(-1))], Now.AddMinutes(-minutesAgo));

        Assert.Equal(needed, FreeGameCache.NeedsDownload(list, Now));
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(70, true)]
    public void NeedsDownload_AnEmptyListAfterAnHour(int minutesAgo, bool needed)
    {
        // Asked right after Epic's change of week, its list may still be the old one,
        // its games filtered out as ended: not kept a whole day.
        Assert.Equal(needed, FreeGameCache.NeedsDownload(new FreeGameList([], Now.AddMinutes(-minutesAgo)), Now));
    }

    [Fact]
    public void NeedsDownload_NotForAGameWithoutEndDate()
    {
        // PS Plus gives none: a fresh list is enough.
        Assert.False(FreeGameCache.NeedsDownload(new FreeGameList([Game("Monthly")], Now.AddHours(-2)), Now));
    }

    [Fact]
    public void Current_LeavesOutTheEndedOffers()
    {
        var list = new FreeGameList([Game("Ended", Now), Game("Free", Now.AddDays(1)), Game("Monthly")], Now.AddDays(-1));

        Assert.Equal(["Free", "Monthly"], FreeGameCache.Current(list, Now).Select(g => g.Title));
    }

    [Fact]
    public void Current_WithoutAList_IsEmpty()
    {
        Assert.Empty(FreeGameCache.Current(null, Now));
    }

    [Fact]
    public void TheKeysAreKnownToTheCleanup()
    {
        var active = CacheCleanup.ActiveKeys(new CacheStore(_folder.Path));

        Assert.Contains(FreeGameCache.EpicKey, active);
        Assert.Contains(FreeGameCache.PsPlusKey, active);
        Assert.Contains(FreeGameCache.AmazonKey, active);
    }
}
