using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.PlayStation;

/// <summary>The PlayStation Store catalog: best sellers of both consoles, pre-orders out, one game per title, rated.</summary>
public class PlayStationStoreCatalogSourceTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// A fake PlayStation Store: each category gives its best sellers, the
    /// pre-order filter its own list, each product page a rating (4.5 by
    /// default, "Action") after a short wait; counts the pages in flight.
    /// </summary>
    private sealed class FakeStore : HttpMessageHandler
    {
        private int _inFlight;
        public List<(string Id, string Name)> Ps5 { get; init; } = [("EP-PS5-A", "Game A"), ("EP-PS5-B", "Game B")];
        public List<(string Id, string Name)> Ps4 { get; init; } = [("EP-PS4-C", "Game C")];
        public List<string> PreOrders { get; init; } = [];
        /// <summary>Rating per product id; 4.5 when absent; null = no rating.</summary>
        public Dictionary<string, double?> Ratings { get; init; } = [];
        public HttpStatusCode DetailsStatus { get; set; } = HttpStatusCode.OK;
        public int MostInFlight { get; private set; }
        public int DetailsAsked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var variables = JsonNode.Parse(query["variables"]!)!;
            if (query["operationName"] == "categoryGridRetrieve")
            {
                string category = variables["id"]!.GetValue<string>();
                bool preOrders = variables["filterBy"]!.AsArray().Any(f => f!.GetValue<string>().Contains("future_release_date"));
                var all = category == PlayStationStoreClient.Ps5Games ? Ps5 : Ps4;
                var listed = preOrders ? all.Where(g => PreOrders.Contains(g.Id)) : all;
                var products = listed.Select(g => $$"""{ "id": "{{g.Id}}", "name": "{{g.Name}}", "storeDisplayClassification": "FULL_GAME", "media": [] }""");
                return Json($$"""{ "data": { "categoryGridRetrieve": { "products": [{{string.Join(",", products)}}] } } }""");
            }

            string id = variables["productId"]!.GetValue<string>();
            int now = Interlocked.Increment(ref _inFlight);
            lock (this)
            {
                MostInFlight = Math.Max(MostInFlight, now);
                DetailsAsked++;
            }
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref _inFlight);
            if (DetailsStatus != HttpStatusCode.OK)
                return new HttpResponseMessage(DetailsStatus) { Content = new StringContent("") };
            double? rating = Ratings.TryGetValue(id, out var r) ? r : 4.5;
            string star = rating is null ? "null" : $$"""{ "averageRating": {{rating.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }""";
            return Json($$"""{ "data": { "productRetrieve": { "localizedGenres": [ { "value": "Action" } ], "starRating": {{star}} } } }""");
        }
    }

    private StoreCatalogSource Source() => PlayStationStoreCatalogSource.Create(new CacheStore(_folder.Path));

    private static PlayStationStoreClient Client(FakeStore store) => new(new HttpClient(store));

    [Fact]
    public async Task RefreshAsync_GivesTheBestSellersOfBothConsolesAsCandidates()
    {
        var catalog = await Source().RefreshAsync(Client(new FakeStore()));

        Assert.Equal(["Game A", "Game C", "Game B"], catalog.Games.Select(g => g.Title));
        var game = catalog.Games[0];
        Assert.Equal("psstore:EP-PS5-A", game.ProductId);
        Assert.Equal("EP-PS5-A", game.PlayStationProductId);
        Assert.Equal(["Action & adventure"], game.Genres);
        Assert.Equal(4.5, game.AverageRating);
        Assert.Equal(PlayStationStoreClient.RatingWeight, game.RatingCount);
        Assert.Equal(CatalogSource.PlayStationStore, game.PrimaryStore);
        Assert.False(game.InGamePass);
    }

    [Fact]
    public async Task RefreshAsync_KeepsOneGamePerTitle_TheBestSoldOne()
    {
        // The PS4 and PS5 versions of a game are two products of the same title.
        var store = new FakeStore
        {
            Ps5 = [("EP-PS5-OTHER", "Other"), ("EP-PS5-ER", "ELDEN RING PS4 & PS5")],
            Ps4 = [("EP-PS4-ER", "ELDEN RING"), ("EP-PS4-X", "Another")]
        };

        var catalog = await Source().RefreshAsync(Client(store));

        // Ranked first on PS4, second on PS5: the PS4 product is the better seller.
        Assert.Equal("EP-PS4-ER", Assert.Single(catalog.Games, g => g.Title.StartsWith("ELDEN RING")).PlayStationProductId);
        Assert.Equal(3, catalog.Games.Count);
        // The pages of the games kept only.
        Assert.Equal(3, store.DetailsAsked);
    }

    [Fact]
    public async Task RefreshAsync_LeavesThePreOrdersOut()
    {
        var store = new FakeStore { PreOrders = ["EP-PS5-B"] };

        var catalog = await Source().RefreshAsync(Client(store));

        Assert.DoesNotContain(catalog.Games, g => g.Title == "Game B");
        Assert.Equal(2, store.DetailsAsked);
    }

    [Fact]
    public async Task RefreshAsync_LeavesGamesRatedBelowThreeOut_KeepsThoseWithoutRating()
    {
        var store = new FakeStore { Ratings = { ["EP-PS5-A"] = 2.64, ["EP-PS5-B"] = null } };

        var catalog = await Source().RefreshAsync(Client(store));

        Assert.Equal(["Game C", "Game B"], catalog.Games.Select(g => g.Title));
        var unrated = catalog.Games.Single(g => g.Title == "Game B");
        Assert.Null(unrated.AverageRating);
        Assert.Equal(0, unrated.RatingCount);
    }

    [Fact]
    public async Task RefreshAsync_AsksEightProductPagesAtOnceAtMost()
    {
        var store = new FakeStore { Ps5 = [.. Enumerable.Range(0, 30).Select(i => ($"EP-{i}", $"Game {i}"))], Ps4 = [] };

        var catalog = await Source().RefreshAsync(Client(store));

        Assert.Equal(30, catalog.Games.Count);
        Assert.InRange(store.MostInFlight, 2, PlayStationStoreClient.MaxDetailsAtOnce);
    }

    [Fact]
    public async Task NoProductPageReadable_KeepsTheOldCatalog()
    {
        // Every page in error: the details query broke, the 3/5 filter would silently stop working.
        var source = Source();
        await source.RefreshAsync(Client(new FakeStore()));

        await Assert.ThrowsAsync<PlayStationException>(() =>
            source.RefreshAsync(Client(new FakeStore { DetailsStatus = HttpStatusCode.InternalServerError })));

        Assert.Equal(3, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public async Task TheRateLimit_StopsEverything_AndKeepsTheOldCatalog()
    {
        var source = Source();
        await source.RefreshAsync(Client(new FakeStore()));

        var ex = await Assert.ThrowsAsync<PlayStationException>(() =>
            source.RefreshAsync(Client(new FakeStore { DetailsStatus = HttpStatusCode.TooManyRequests })));

        Assert.True(ex.IsRateLimited);
        Assert.Equal(3, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public async Task AnEmptyCatalog_IsRefused()
    {
        var source = Source();

        await Assert.ThrowsAsync<PlayStationException>(() => source.RefreshAsync(Client(new FakeStore { Ps5 = [], Ps4 = [] })));

        Assert.Null(source.LoadCached());
    }

    [Fact]
    public void ACatalogWithoutPlayStationStoreProducts_IsReadAsBefore()
    {
        // A cached catalog of 1.5.x: no PlayStationProductId written.
        var game = JsonSerializer.Deserialize("""{"ProductId":"epic:ns","Title":"T","XboxTitleId":null,"PackageFamilyName":null,"Genres":["Strategy"],"OnPc":false,"OnConsole":false,"LeavingSoon":false,"EpicStoreLink":"https://store.epicgames.com/p/t"}""",
            CoreJsonContext.Default.CatalogGame)!;

        Assert.Null(game.PlayStationProductId);
        Assert.Equal(CatalogSource.Epic, game.PrimaryStore);
        // Not written when null: the cache format stays as it was.
        Assert.DoesNotContain("PlayStationProductId", JsonSerializer.Serialize(game, CoreJsonContext.Default.CatalogGame));
    }

    [Fact]
    public void TheCacheKey_IsKnownToTheCleanup()
    {
        Assert.Contains(PlayStationStoreCatalogSource.CacheKey, CacheCleanup.ActiveKeys(new CacheStore(_folder.Path)));
    }
}
