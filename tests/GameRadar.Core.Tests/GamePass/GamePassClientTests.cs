using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.GamePass;

public class GamePassClientTests
{
    [Fact]
    public void ParseListJson_SkipsHeaderAndReturnsProductIds()
    {
        const string json = """
            [
              { "siglId": "fdd9e2a7", "title": "Tous les jeux PC" },
              { "id": "9NPDN9R45JX4" },
              { "id": "9MSVBF0KZFVW" }
            ]
            """;

        Assert.Equal(["9NPDN9R45JX4", "9MSVBF0KZFVW"], GamePassClient.ParseListJson(json));
    }

    // Minimal Store answer. RatingCount is a string on purpose: the Store
    // sometimes sends numbers that way.
    private const string ProductsJson = """
        {
          "Products": [
            {
              "ProductId": "P1",
              "LocalizedProperties": [ { "ProductTitle": " Test Game " } ],
              "Properties": {
                "Category": "Shooter",
                "Categories": [ "Shooter", "Role playing" ],
                "PackageFamilyName": "Pub.Test_abc",
                "Attributes": [
                  { "Name": "SinglePlayer" },
                  { "Name": "XblCrossPlatformCoop" },
                  { "Name": "Capability4k" }
                ]
              },
              "AlternateIds": [
                { "IdType": "LegacyXboxProductId", "Value": "x" },
                { "IdType": "XboxTitleId", "Value": "111" }
              ],
              "MarketProperties": [ {
                "UsageData": [
                  { "AggregateTimeSpan": "7Days", "AverageRating": 3.0, "RatingCount": 2 },
                  { "AggregateTimeSpan": "AllTime", "AverageRating": 4.6, "RatingCount": "1200" }
                ]
              } ]
            },
            {
              "ProductId": "P2",
              "LocalizedProperties": [ { "ProductTitle": "Unrated" } ],
              "Properties": { "Category": "Puzzle & trivia" }
            },
            { "ProductId": "P3", "LocalizedProperties": [] }
          ]
        }
        """;

    private static readonly HashSet<string> PcIds = ["P1", "P2"];
    private static readonly HashSet<string> ConsoleIds = ["P1"];
    private static readonly HashSet<string> LeavingIds = ["P2"];

    [Theory]
    // Titled 16:9 art wins over everything else.
    [InlineData("""[ {"ImagePurpose":"BoxArt","Uri":"//img/box"}, {"ImagePurpose":"TitledHeroArt","Uri":"//img/titled"}, {"ImagePurpose":"SuperHeroArt","Uri":"//img/hero"} ]""", "https://img/titled")]
    // Then the same art without title, then the square box art.
    [InlineData("""[ {"ImagePurpose":"BoxArt","Uri":"//img/box"}, {"ImagePurpose":"SuperHeroArt","Uri":"//img/hero"} ]""", "https://img/hero")]
    [InlineData("""[ {"ImagePurpose":"Screenshot","Uri":"//img/shot"}, {"ImagePurpose":"BoxArt","Uri":"//img/box"} ]""", "https://img/box")]
    // Unwanted kinds only, or a wanted kind without address: no picture.
    [InlineData("""[ {"ImagePurpose":"Screenshot","Uri":"//img/shot"}, {"ImagePurpose":"TitledHeroArt","Uri":""} ]""", null)]
    public void ParseProductsJson_PicksTheBestCardPicture(string imagesJson, string? expectedUrl)
    {
        string json = $$"""
            { "Products": [ {
                "ProductId": "P1",
                "LocalizedProperties": [ { "ProductTitle": "Game", "Images": {{imagesJson}} } ]
            } ] }
            """;

        var game = GamePassClient.ParseProductsJson(json, PcIds, ConsoleIds, LeavingIds).Single();

        Assert.Equal(expectedUrl, game.ImageUrl);
    }

    [Fact]
    public void ParseProductsJson_ReadsTitleIdsAndGenresMainFirst()
    {
        var game = GamePassClient.ParseProductsJson(ProductsJson, PcIds, ConsoleIds, LeavingIds).First();

        Assert.Equal("Test Game", game.Title);
        Assert.Equal("111", game.XboxTitleId);
        Assert.Equal("Pub.Test_abc", game.PackageFamilyName);
        Assert.Equal(["Shooter", "Role playing"], game.Genres);
    }

    [Theory]
    [InlineData("SinglePlayer", PlayModes.Solo)]
    [InlineData("XblOnlineCoop", PlayModes.CoopOnline)]
    [InlineData("XblCrossPlatformCoop", PlayModes.CoopOnline)]
    [InlineData("XblCrossPlatformMultiPlayer", PlayModes.MultiOnline)]
    [InlineData("SharedSplitScreen", PlayModes.Local)]
    [InlineData("Capability4k", PlayModes.None)]
    [InlineData("XblCloudSaves", PlayModes.None)]
    public void ModeOf_MapsStoreAttributesToPlayModes(string attribute, PlayModes expected)
    {
        Assert.Equal(expected, GamePassClient.ModeOf(attribute));
    }

    [Fact]
    public void ParseProductsJson_CombinesPlayModesAndIgnoresTechnicalAttributes()
    {
        var games = GamePassClient.ParseProductsJson(ProductsJson, PcIds, ConsoleIds, LeavingIds).ToList();

        Assert.Equal(PlayModes.Solo | PlayModes.CoopOnline, games[0].Modes);
        Assert.Equal(PlayModes.None, games[1].Modes);
    }

    [Fact]
    public void ParseProductsJson_UsesAllTimeRating()
    {
        var game = GamePassClient.ParseProductsJson(ProductsJson, PcIds, ConsoleIds, LeavingIds).First();

        Assert.Equal(4.6, game.AverageRating);
        Assert.Equal(1200, game.RatingCount);
    }

    [Fact]
    public void ParseProductsJson_SetsPlatformAndLeavingFlagsFromLists()
    {
        var games = GamePassClient.ParseProductsJson(ProductsJson, PcIds, ConsoleIds, LeavingIds).ToList();

        Assert.True(games[0].OnPc && games[0].OnConsole && !games[0].LeavingSoon);
        Assert.True(games[1].OnPc && !games[1].OnConsole && games[1].LeavingSoon);
    }

    [Fact]
    public void ParseProductsJson_HandlesMissingRatingAndSkipsUntitledProducts()
    {
        var games = GamePassClient.ParseProductsJson(ProductsJson, PcIds, ConsoleIds, LeavingIds).ToList();

        Assert.Equal(2, games.Count);
        Assert.Null(games[1].AverageRating);
        Assert.Equal(0, games[1].RatingCount);
    }

    [Fact]
    public async Task FetchCatalog_RecordsWhichPlansIncludeEachGameOnEachPlatform()
    {
        static string List(params string[] ids) =>
            "[{\"siglId\":\"x\",\"title\":\"t\"}" + string.Concat(ids.Select(id => $",{{\"id\":\"{id}\"}}")) + "]";
        var requested = new List<string>();
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            string query = Uri.UnescapeDataString(uri.Query);
            requested.Add(query);
            bool pc = query.Contains("platformContext=pc");
            if (uri.AbsolutePath.Contains("products"))
                return ProductsJson;
            if (uri.AbsolutePath.EndsWith("/v2"))
                return List("P2"); // leaving soon
            return query switch
            {
                _ when query.Contains("97c6c862") => pc ? List("P1", "P2") : List("P1"),   // Ultimate
                _ when query.Contains("09a72c0d") => List("P1"),                            // Premium, both platforms
                _ when query.Contains("609d944c") => List("P2"),                            // PC Game Pass
                _ => List()                                                                 // Essential
            };
        }));

        var games = await new GamePassClient(http).FetchCatalogAsync();

        var p1 = games.Single(g => g.ProductId == "P1");
        Assert.Equal(GamePassTier.Ultimate | GamePassTier.Premium, p1.ConsoleTiers);
        Assert.Equal(GamePassTier.Ultimate | GamePassTier.Premium, p1.PcTiers);
        var p2 = games.Single(g => g.ProductId == "P2");
        Assert.Equal(GamePassTier.None, p2.ConsoleTiers);
        Assert.Equal(GamePassTier.Ultimate | GamePassTier.PcGamePass, p2.PcTiers);
        Assert.True(p2.OnPc && !p2.OnConsole && p2.LeavingSoon);
        // 7 plan lists (with their subscription context) + leaving soon + 1 batch of products.
        Assert.Equal(9, requested.Count);
        Assert.Contains(requested, q => q.Contains("subscriptionContext=cfq7ttc0kgq8") && q.Contains("platformContext=pc"));
        // English titles, like every other source: games are matched by title.
        Assert.All(requested, q => Assert.Contains("en-us", q));
    }

    [Fact]
    public async Task Refresh_KeepsTheCatalogWhenAPlanThatHadGamesComesBackEmpty()
    {
        static string List(params string[] ids) =>
            "[{\"siglId\":\"x\",\"title\":\"t\"}" + string.Concat(ids.Select(id => $",{{\"id\":\"{id}\"}}")) + "]";
        bool ultimateEmpty = false;
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            string query = Uri.UnescapeDataString(uri.Query);
            if (uri.AbsolutePath.Contains("products"))
                return ProductsJson;
            if (query.Contains("97c6c862"))
                return ultimateEmpty ? List() : List("P1");   // Ultimate
            return query.Contains("09a72c0d") ? List("P1") : List();   // Premium; the others empty
        }));
        using var folder = new TempFolder();
        var source = new GamePassCatalogSource(new CacheStore(folder.Path));
        await source.RefreshAsync(new GamePassClient(http));

        // The Ultimate list answers only its header (an id changed...): the
        // catalog would lose every Ultimate game for a week.
        ultimateEmpty = true;
        await Assert.ThrowsAsync<GamePassException>(() => source.RefreshAsync(new GamePassClient(http)));

        Assert.Equal(GamePassTier.Ultimate | GamePassTier.Premium, source.LoadCached()!.Games.Single(g => g.ProductId == "P1").ConsoleTiers);
    }

    /// <summary>Answers each request with the given JSON.</summary>
    private sealed class FakeHandler(Func<Uri, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "application/json")
            });
    }
}
