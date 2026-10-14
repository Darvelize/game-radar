using System.Net;
using System.Text;
using GameRadar.Core.Models;
using GameRadar.Core.Steam;

using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Steam;

public class SteamCatalogClientTests
{
    private const string TagListJson = """
        { "response": { "tags": [
            { "tagid": 19, "name": "Action" },
            { "tagid": 113, "name": "Free to Play" },
            { "tagid": 122, "name": "RPG" },
            { "tagid": 1663, "name": "FPS" },
            { "tagid": 3942, "name": "Sci-fi" },
            { "tagid": 12095, "name": "Sexual Content" }
        ] } }
        """;

    private static readonly IReadOnlyDictionary<int, string> Tags = SteamCatalogClient.ParseTagList(TagListJson);

    /// <summary>One store item, with only what a test changes.</summary>
    private static string Item(
        int appId,
        string name = "A Game",
        int reviews = 10_000,
        int positive = 90,
        string tags = """[{"tagid":1663,"weight":900},{"tagid":122,"weight":500}]""",
        string players = "[2,38]",
        string descriptors = "[]") => $$"""
        { "success": 1, "appid": {{appId}}, "name": "{{name}}", "content_descriptorids": {{descriptors}},
          "tags": {{tags}},
          "categories": { "supported_player_categoryids": {{players}} },
          "reviews": { "summary_filtered": { "review_count": {{reviews}}, "percent_positive": {{positive}} } },
          "assets": { "asset_url_format": "steam/apps/{{appId}}/${FILENAME}?t=1", "main_capsule": "abc/capsule_616x353.jpg" } }
        """;

    private static string Page(params string[] items) =>
        $$"""{ "response": { "store_items": [{{string.Join(",", items)}}] } }""";

    [Fact]
    public void ParseQuery_TranslatesAGame()
    {
        var game = Assert.Single(SteamCatalogClient.ParseQuery(Page(Item(1085660, "Destiny 2")), Tags));

        Assert.Equal("steam:1085660", game.ProductId);
        Assert.Equal("1085660", game.SteamAppId);
        Assert.Equal("Destiny 2", game.Title);
        // Most voted tag first: "FPS" says Shooter, "RPG" says Role playing.
        Assert.Equal(["Shooter", "Role playing"], game.Genres);
        Assert.Equal(PlayModes.Solo | PlayModes.CoopOnline, game.Modes);
        Assert.Equal(4.5, game.AverageRating);
        Assert.Equal(10_000, game.RatingCount);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1085660/abc/capsule_616x353.jpg?t=1", game.ImageUrl);
        Assert.False(game.InGamePass);
        Assert.Equal(CatalogSource.Steam, game.PrimaryStore);
    }

    [Fact]
    public void ParseQuery_OrdersGenresByTagWeight()
    {
        string tags = """[{"tagid":3942,"weight":999},{"tagid":122,"weight":800},{"tagid":19,"weight":700},{"tagid":1663,"weight":10}]""";

        var game = Assert.Single(SteamCatalogClient.ParseQuery(Page(Item(1, tags: tags)), Tags));

        // "Sci-fi" is not a genre: skipped. "FPS" comes last, by weight.
        Assert.Equal(["Role playing", "Action & adventure", "Shooter"], game.Genres);
    }

    [Theory]
    [InlineData(499, 90)]   // too little known
    [InlineData(10_000, 69)] // "Mixed" reviews
    public void ParseQuery_SkipsLittleKnownOrPoorlyRatedGames(int reviews, int positive)
    {
        Assert.Empty(SteamCatalogClient.ParseQuery(Page(Item(1, reviews: reviews, positive: positive)), Tags));
    }

    [Fact]
    public void ParseQuery_SkipsAdultGames()
    {
        Assert.Empty(SteamCatalogClient.ParseQuery(Page(Item(1, descriptors: "[1,4]")), Tags));
    }

    [Fact]
    public void ParseQuery_SkipsGamesWithoutAnyGenreTag()
    {
        string tags = """[{"tagid":113,"weight":900},{"tagid":3942,"weight":500}]""";

        Assert.Empty(SteamCatalogClient.ParseQuery(Page(Item(1, tags: tags)), Tags));
    }

    [Theory]
    [InlineData(2, PlayModes.Solo)]
    [InlineData(38, PlayModes.CoopOnline)]
    [InlineData(36, PlayModes.MultiOnline)]
    [InlineData(39, PlayModes.Local)]
    [InlineData(1, PlayModes.None)]  // "Multi-player": online or local?
    [InlineData(9, PlayModes.None)]  // "Co-op": same
    public void ModeOfCategory_KeepsOnlyPreciseModes(int categoryId, PlayModes expected)
    {
        Assert.Equal(expected, SteamCatalogClient.ModeOfCategory(categoryId));
    }

    [Theory]
    [InlineData("Looter Shooter", "Shooter")]
    [InlineData("Souls-like", "Role playing")]
    [InlineData("Metroidvania", "Platformer")]
    [InlineData("City Builder", "Simulation")]
    [InlineData("Free to Play", null)]
    [InlineData("Indie", null)]
    [InlineData("Atmospheric", null)]
    public void GenreOfTag_TranslatesGenreTagsOnly(string tag, string? expected)
    {
        Assert.Equal(expected, SteamCatalogClient.GenreOfTag(tag));
    }

    [Fact]
    public async Task FetchCatalog_ReadsTagsThenEveryPageWithoutDuplicates()
    {
        var requested = new List<string>();
        var queries = new List<string>();
        var handler = new FakeHandler(uri =>
        {
            requested.Add(uri.AbsolutePath);
            if (uri.AbsolutePath.Contains("Query"))
                queries.Add(Uri.UnescapeDataString(uri.Query));
            if (uri.AbsolutePath.Contains("GetTagList"))
                return TagListJson;
            // Game 2 shows up on both pages (it moved between the two requests).
            return uri.Query.Contains("%22start%22%3A0")
                ? Page(Item(1), Item(2))
                : Page(Item(2), Item(3));
        });
        using var http = new HttpClient(handler);

        var games = await new SteamCatalogClient(http).FetchCatalogAsync();

        Assert.Equal(["steam:1", "steam:2", "steam:3"], games.Select(g => g.ProductId));
        Assert.Equal(1 + SteamCatalogClient.PageCount, requested.Count);
        // English names, like every other source (the country stays France).
        Assert.All(queries, q => Assert.Contains("\"language\":\"english\",\"country_code\":\"FR\"", q));
        Assert.Equal(SteamCatalogClient.PageCount, queries.Count);
    }

    [Fact]
    public async Task FetchCatalog_ExplainsAnUnavailableStore()
    {
        using var http = new HttpClient(new FakeHandler(_ => null));

        await Assert.ThrowsAsync<SteamException>(() => new SteamCatalogClient(http).FetchCatalogAsync());
    }

    /// <summary>Answers each request with the given JSON, or 503 for null.</summary>
    private sealed class FakeHandler(Func<Uri, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? json = answer(request.RequestUri!);
            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(json));
        }
    }
}
