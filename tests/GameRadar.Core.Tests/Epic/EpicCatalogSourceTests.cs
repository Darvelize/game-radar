using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GameRadar.Core.Cache;
using GameRadar.Core.Epic;
using GameRadar.Core.Resources;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Epic;

public class EpicCatalogSourceTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// A fake Epic store: each collection gives its games (the first one in all
    /// three), each rating 4.5 after a short wait; counts the ratings in flight.
    /// </summary>
    private sealed class FakeStore : HttpMessageHandler
    {
        private int _inFlight;
        public int GamesPerCollection { get; init; } = 10;
        public HttpStatusCode CollectionStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode RatingStatus { get; set; } = HttpStatusCode.OK;
        /// <summary>Only these games' ratings fail (all when null).</summary>
        public Func<string, bool>? RatingFails { get; init; }
        /// <summary>A collection Epic no longer has: answered without its layout.</summary>
        public string? RetiredCollection { get; init; }
        public int MostInFlight { get; private set; }
        public int Ratings { get; private set; }
        public List<(string Agent, bool HasAuthorization)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests)
                Requests.Add((request.Headers.UserAgent.ToString(), request.Headers.Authorization is not null));
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            var variables = body["variables"]!;
            if (variables["slug"] is { } slug)
            {
                if (CollectionStatus != HttpStatusCode.OK)
                    return new HttpResponseMessage(CollectionStatus) { Content = new StringContent("") };
                string name = slug.GetValue<string>();
                if (name == RetiredCollection)
                    return Json("""{ "data": { "Storefront": { "collectionLayout": null } } }""");
                var offers = Enumerable.Range(0, GamesPerCollection).Select(i => i == 0 ? "ns-shared" : $"ns-{name}-{i}")
                    .Select(ns => $$"""{ "title": "Game {{ns}}", "namespace": "{{ns}}", "categories": [{"path":"games"}], "tags": [{"name":"RPG","groupName":"genre"}], "keyImages": [], "catalogNs": { "mappings": [ { "pageSlug": "{{ns}}" } ] } }""");
                return Json($$"""{ "data": { "Storefront": { "collectionLayout": { "collectionOffers": [{{string.Join(",", offers)}}] } } } }""");
            }

            string ns = variables["sandboxId"]!.GetValue<string>();
            int now = Interlocked.Increment(ref _inFlight);
            lock (Requests)
            {
                MostInFlight = Math.Max(MostInFlight, now);
                Ratings++;
            }
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref _inFlight);
            return RatingStatus == HttpStatusCode.OK || RatingFails?.Invoke(ns) == false
                ? Json("""{"data":{"RatingsPolls":{"getProductResult":{"averageRating":4.5}}}}""")
                : new HttpResponseMessage(RatingStatus) { Content = new StringContent("") };
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private EpicCatalogSource Source() => new(new CacheStore(_folder.Path));

    [Fact]
    public async Task RefreshAsync_ReadsTheThreeCollectionsOnceEachGameAndAsTheLauncher()
    {
        var store = new FakeStore();

        var catalog = await Source().RefreshAsync(new EpicCatalogClient(new HttpClient(store)));

        // 3 × 10 games, the first one in all three: 28 distinct, one rating each.
        Assert.Equal(28, catalog.Games.Count);
        Assert.Equal(28, store.Ratings);
        Assert.All(catalog.Games, g => Assert.Equal(4.5, g.AverageRating));
        Assert.All(store.Requests, r => Assert.StartsWith("EpicGamesLauncher/", r.Agent));
        // The catalog is read without account: no token ever sent.
        Assert.DoesNotContain(store.Requests, r => r.HasAuthorization);
    }

    [Fact]
    public async Task TheRatings_AreAskedEightAtATimeAtMost()
    {
        var store = new FakeStore { GamesPerCollection = 40 };

        await Source().RefreshAsync(new EpicCatalogClient(new HttpClient(store)));

        Assert.InRange(store.MostInFlight, 2, 8);
    }

    [Fact]
    public async Task ARatingInError_KeepsTheGameWithoutRating()
    {
        var store = new FakeStore { RatingStatus = HttpStatusCode.InternalServerError, RatingFails = ns => ns.EndsWith("-1") };

        var catalog = await Source().RefreshAsync(new EpicCatalogClient(new HttpClient(store)));

        Assert.Equal(28, catalog.Games.Count);
        Assert.Equal(3, catalog.Games.Count(g => g.AverageRating is null));
    }

    [Fact]
    public async Task NotOneRating_IsABrokenQueryAndKeepsTheOldCatalog()
    {
        var source = Source();
        await source.RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore())));

        // Saved, the 3/5 filter would silently stop working.
        var ex = await Assert.ThrowsAsync<EpicException>(() => source.RefreshAsync(
            new EpicCatalogClient(new HttpClient(new FakeStore { RatingStatus = HttpStatusCode.InternalServerError }))));

        Assert.Equal(CoreStrings.Epic_Format, ex.Message);
        Assert.All(source.LoadCached()!.Games, g => Assert.Equal(4.5, g.AverageRating));
    }

    [Fact]
    public async Task ACollectionEpicNoLongerHas_LeavesTheTwoOthers()
    {
        var catalog = await Source().RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore { RetiredCollection = "most-played" })));

        // 2 × 10 games, the first one in both: 19.
        Assert.Equal(19, catalog.Games.Count);
    }

    [Fact]
    public async Task ARateLimit_StopsAndKeepsTheOldCatalog()
    {
        var source = Source();
        await source.RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore())));

        var ex = await Assert.ThrowsAsync<EpicException>(() =>
            source.RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore { RatingStatus = HttpStatusCode.TooManyRequests }))));

        Assert.True(ex.IsRateLimited);
        Assert.Equal(28, source.LoadCached()!.Games.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ACollectionRefused_IsExplained(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<EpicException>(() =>
            Source().RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore { CollectionStatus = status }))));

        Assert.Equal(CoreStrings.Epic_StoreDown, ex.Message);
    }

    [Fact]
    public async Task AnEmptyCatalog_KeepsTheOldOne()
    {
        var source = Source();
        await source.RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore())));

        var ex = await Assert.ThrowsAsync<EpicException>(() =>
            source.RefreshAsync(new EpicCatalogClient(new HttpClient(new FakeStore { GamesPerCollection = 0 }))));

        Assert.Equal(CoreStrings.Epic_EmptyCatalog, ex.Message);
        Assert.Equal(28, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public void TheCacheKey_IsKnownToTheCleanup()
    {
        Assert.Contains(EpicCatalogSource.CacheKey, CacheCleanup.ActiveKeys(new CacheStore(_folder.Path)));
    }
}
