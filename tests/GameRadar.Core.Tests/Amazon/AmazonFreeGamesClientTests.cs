using System.Net;
using System.Text;
using GameRadar.Core.Amazon;

namespace GameRadar.Core.Tests.Amazon;

public class AmazonFreeGamesClientTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    // The public claims page, cut down to what is read: the form's token.
    private const string Page = """
        <html><body><form><input type='hidden' name='csrf-key' value='tok+en/123==' /></form></body></html>
        """;

    /// <summary>One item of the free games collection, with only what a test changes.</summary>
    private static string Item(
        string title,
        string slug = "a-game-gog",
        string category = "FULL_GAME",
        string start = "2026-09-17T17:00:00Z",
        string end = "2026-10-21T17:00:00Z",
        string host = "gaming.amazon.com") => $$"""
        { "category": "{{category}}",
          "assets": { "title": "{{title}}",
                      "externalClaimLink": "https://{{host}}/{{slug}}/dp/amzn1.pg.item.1?ref_=SM_X",
                      "cardMedia": { "defaultMedia": { "src1x": "https://m.media-amazon.com/images/I/71iiIFosTLL._SX256_._AGmerlin_._FMwebp_.jpg" } } },
          "offers": [ { "startTime": "{{start}}", "endTime": "{{end}}" } ] }
        """;

    private static string Items(params string[] items) =>
        $$"""{ "data": { "items": { "items": [{{string.Join(",", items)}}] } } }""";

    [Fact]
    public void ReadToken_FindsTheFormsToken()
    {
        Assert.Equal("tok+en/123==", AmazonFreeGamesClient.ReadToken(Page));
    }

    [Fact]
    public void ReadToken_APageWithoutToken_IsAnAmazonError()
    {
        Assert.Throws<AmazonException>(() => AmazonFreeGamesClient.ReadToken("<html>Sorry, something went wrong</html>"));
    }

    [Fact]
    public void ParseJson_ReadsAGame()
    {
        var game = Assert.Single(AmazonFreeGamesClient.ParseJson(Items(Item("WarBreeds", slug: "warbreeds-gog")), Now));

        Assert.Equal("WarBreeds", game.Title);
        Assert.Equal("https://gaming.amazon.com/warbreeds-gog/dp/amzn1.pg.item.1?ref_=SM_X", game.Link);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 17, 0, 0, TimeSpan.Zero), game.Until);
        // The original picture rather than the small WebP (measured: a 95 KB JPEG).
        Assert.Equal("https://m.media-amazon.com/images/I/71iiIFosTLL.jpg", game.ImageUrl);
        Assert.Equal("GOG", game.RedeemOn);
    }

    [Theory]
    [InlineData("doom-gog", "GOG")]
    [InlineData("hue-epic", "Epic Games")]
    [InlineData("havendock-aga", "Amazon Games")]
    [InlineData("doom-eternal-microsoft", "Microsoft Store")]
    [InlineData("the-da-vinci-cryptex-legacy", "Legacy Games")]
    [InlineData("a-game-somewhere", null)]
    public void ParseJson_TellsWhereToRedeemFromTheLink(string slug, string? redeemOn)
    {
        Assert.Equal(redeemOn, Assert.Single(AmazonFreeGamesClient.ParseJson(Items(Item("A game", slug: slug)), Now)).RedeemOn);
    }

    [Fact]
    public void ParseJson_KeepsOnlyFullGamesFreeNow()
    {
        string json = Items(
            Item("Loot", category: "IN_GAME_LOOT"),
            Item("Not yet", start: "2026-09-24T17:00:00Z"),
            Item("Ended", end: "2026-09-20T17:00:00Z"),
            Item("Elsewhere", host: "example.com"),
            Item("Free"));

        Assert.Equal(["Free"], AmazonFreeGamesClient.ParseJson(json, Now).Select(g => g.Title));
    }

    [Fact]
    public void ParseJson_SortsByTheEndOfTheOffer()
    {
        string json = Items(Item("Later", end: "2027-01-08T18:00:00Z"), Item("Sooner"));

        Assert.Equal(["Sooner", "Later"], AmazonFreeGamesClient.ParseJson(json, Now).Select(g => g.Title));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "data": null, "errors": [ { "message": "Unauthorized" } ] }""")]
    public void ParseJson_AnUnexpectedAnswer_IsAnAmazonError(string json)
    {
        Assert.Throws<AmazonException>(() => AmazonFreeGamesClient.ParseJson(json, Now));
    }

    /// <summary>The page, then the GraphQL answer; records what the GraphQL request sent.</summary>
    private sealed class FakeLuna(string graphQl, HttpStatusCode graphQlStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Query { get; private set; }
        public string? QueryBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                var page = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page, Encoding.UTF8, "text/html") };
                page.Headers.Add("Set-Cookie", "session-id=140-1; Domain=.amazon.com; Path=/; Secure");
                page.Headers.Add("Set-Cookie", "session-id-time=2082787201l; Domain=.amazon.com; Path=/; Secure");
                return page;
            }
            Query = request;
            QueryBody = await request.Content!.ReadAsStringAsync(ct);
            return TestData.Json(graphQl, graphQlStatus);
        }
    }

    [Fact]
    public async Task FetchAsync_SendsTheAnonymousSessionAndTheSitesIdentity()
    {
        var luna = new FakeLuna(Items(Item("Free")));
        using var http = new HttpClient(luna);

        var games = await new AmazonFreeGamesClient(http).FetchAsync(Now);

        Assert.Single(games);
        var query = luna.Query!;
        Assert.Equal("https://luna.amazon.com/graphql", query.RequestUri!.ToString());
        Assert.Equal("tok+en/123==", Assert.Single(query.Headers.GetValues("csrf-token")));
        Assert.Equal("CarboniteApp", Assert.Single(query.Headers.GetValues("Client-Id")));
        Assert.Equal("session-id=140-1; session-id-time=2082787201l", string.Join("; ", query.Headers.GetValues("Cookie")));
        Assert.Contains("FREE_GAMES", luna.QueryBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task FetchAsync_AnHttpError_IsAnAmazonError(HttpStatusCode status, bool rateLimited)
    {
        using var http = new HttpClient(new FakeLuna("", status));

        var error = await Assert.ThrowsAsync<AmazonException>(() => new AmazonFreeGamesClient(http).FetchAsync(Now));

        Assert.Equal(rateLimited, error.IsRateLimited);
    }
}
