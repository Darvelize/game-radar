using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.GOG;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.GOG;

public class GogCatalogSourceTests
{
    private static string Product(string id, string title) => $$"""
        { "id": "{{id}}", "productType": "game", "title": "{{title}}",
          "genres": [{ "name": "Strategy", "slug": "strategy" }], "tags": [], "features": [],
          "reviewsRating": 45, "reviewsCount": 1000, "storeLink": "https://www.gog.com/en/game/{{id}}" }
        """;

    private static string Page(params string[] products) =>
        $$"""{ "pages": 83, "products": [{{string.Join(",", products)}}] }""";

    [Fact]
    public async Task FetchCatalogAsync_ReadsTenPagesOfBestSellingGames()
    {
        var asked = new List<Uri>();
        var client = new GogCatalogClient(new HttpClient(new FakeHandler(uri =>
        {
            asked.Add(uri);
            // Page n holds game n; page 2 repeats game 1 (it moved between two requests).
            string page = PageOf(uri);
            return page == "2" ? Page(Product("1", "Game 1")) : Page(Product(page, "Game " + page));
        })));

        var games = await client.FetchCatalogAsync();

        Assert.Equal(GogCatalogClient.PageCount, asked.Count);
        Assert.All(asked, uri =>
        {
            Assert.Equal("catalog.gog.com", uri.Host);
            Assert.Contains("limit=100", uri.Query);
            Assert.Contains("order=desc:bestselling", uri.Query);
            Assert.Contains("productType=in:game", uri.Query);
            Assert.Contains("locale=en-US", uri.Query);
        });
        Assert.Equal(Enumerable.Range(1, 10).Select(n => n.ToString()), asked.Select(PageOf));
        Assert.Equal(GogCatalogClient.PageCount - 1, games.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task FetchCatalogAsync_AnErrorStatus_ThrowsGogException(HttpStatusCode status)
    {
        var client = new GogCatalogClient(new HttpClient(new FakeHandler(_ => "", status)));

        await Assert.ThrowsAsync<GogException>(() => client.FetchCatalogAsync());
    }

    [Fact]
    public async Task RefreshAsync_SavesTheCatalogAndLoadCachedReadsItBack()
    {
        using var folder = new TempFolder();
        var source = new GogCatalogSource(new CacheStore(folder.Path));
        Assert.Null(source.LoadCached());

        await source.RefreshAsync(ClientAnswering(Page(Product("1", "Disco Elysium"))));

        var game = Assert.Single(source.LoadCached()!.Games);
        Assert.Equal("Disco Elysium", game.Title);
        Assert.Equal("https://www.gog.com/en/game/1", game.GogStoreLink);
        Assert.Equal(CatalogSource.Gog, game.PrimaryStore);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheOldCatalogWhenTheNewOneIsEmpty()
    {
        using var folder = new TempFolder();
        var source = new GogCatalogSource(new CacheStore(folder.Path));
        await source.RefreshAsync(ClientAnswering(Page(Product("1", "Disco Elysium"))));

        await Assert.ThrowsAsync<GogException>(() => source.RefreshAsync(ClientAnswering(Page())));

        Assert.Single(source.LoadCached()!.Games);
    }

    [Fact]
    public void IsStale_AfterSevenDays()
    {
        var fetched = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var catalog = new GameCatalog([], fetched);

        Assert.False(GogCatalogSource.IsStale(catalog, fetched.AddDays(6)));
        Assert.True(GogCatalogSource.IsStale(catalog, fetched.AddDays(8)));
    }

    private static string PageOf(Uri uri) => uri.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("page="))["page=".Length..];

    private static GogCatalogClient ClientAnswering(string page) => new(new HttpClient(new FakeHandler(_ => page)));

    private sealed class FakeHandler(Func<Uri, string> answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "application/json")
            });
    }
}
