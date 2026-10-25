using GameRadar.Core.Resources;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Tests.Steam;

public class SteamFreeGamesClientTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    /// <summary>One store item of a Query answer, with only what a test changes.</summary>
    private static string Item(int appId, string name, string? purchase = null, string? weekend = null, string descriptors = "[]", int success = 1) => $$"""
        { "success": {{success}}, "appid": {{appId}}, "name": "{{name}}", "type": 0,
          "content_descriptorids": {{descriptors}},
          "assets": { "asset_url_format": "steam/apps/{{appId}}/${FILENAME}?t=1", "main_capsule": "capsule_616x353.jpg" },
          "best_purchase_option": {{purchase ?? "null"}},
          "free_weekend": {{weekend ?? "null"}} }
        """;

    private static string FreeToKeep(DateTimeOffset ends, bool toKeep = true, int discount = 100) =>
        $$"""{ "discount_pct": {{discount}}, "final_price_in_cents": "0", "is_free_to_keep": {{(toKeep ? "true" : "false")}}, "free_to_keep_ends": {{ends.ToUnixTimeSeconds()}} }""";

    private static string Weekend(DateTimeOffset start, DateTimeOffset end) =>
        $$"""{ "start_time": {{start.ToUnixTimeSeconds()}}, "end_time": {{end.ToUnixTimeSeconds()}} }""";

    private static string Query(params string[] items) =>
        $$"""{ "response": { "metadata": { "total_matching_records": {{items.Length}} }, "store_items": [{{string.Join(",", items)}}] } }""";

    [Fact]
    public void ParseFreeToKeep_ReadsAGameGivenAway()
    {
        var ends = Now.AddDays(2);

        var game = Assert.Single(SteamFreeGamesClient.ParseFreeToKeep(Query(Item(10, "Portal", FreeToKeep(ends))), Now));

        Assert.Equal("Portal", game.Title);
        Assert.Equal(ends, game.Until);
        // Without language: the page adds the app's.
        Assert.Equal("https://store.steampowered.com/app/10/", game.Link);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/10/capsule_616x353.jpg?t=1", game.ImageUrl);
        Assert.False(game.PlayOnly);
    }

    [Fact]
    public void ParseFreeToKeep_LeavesOutWhatIsNotFreeToKeepNow()
    {
        string json = Query(
            Item(1, "Ended", FreeToKeep(Now.AddMinutes(-1))),
            Item(2, "On sale", FreeToKeep(Now.AddDays(2), toKeep: false, discount: 99)),
            Item(3, "Adult", FreeToKeep(Now.AddDays(2)), descriptors: "[1, 3]"),
            Item(4, "Unknown", FreeToKeep(Now.AddDays(2)), success: 0),
            Item(5, "No price"),
            Item(6, "Kept", FreeToKeep(Now.AddDays(1))));

        Assert.Equal(["Kept"], SteamFreeGamesClient.ParseFreeToKeep(json, Now).Select(g => g.Title));
    }

    [Fact]
    public void ParseFreeToKeep_AGameGivenAwayWithoutAnEnd_IsShownWithoutDate()
    {
        string json = Query(Item(10, "Portal", """{ "is_free_to_keep": true }"""));

        Assert.Null(Assert.Single(SteamFreeGamesClient.ParseFreeToKeep(json, Now)).Until);
    }

    [Fact]
    public void ParseFreeToKeep_NoGameGivenAway_IsAnEmptyList()
    {
        // Measured: no match, no "store_items" at all.
        Assert.Empty(SteamFreeGamesClient.ParseFreeToKeep("""{ "response": { "metadata": { "total_matching_records": 0 } } }""", Now));
    }

    [Fact]
    public void ParseFreeToKeep_AnAnswerWithoutResponse_Throws()
    {
        Assert.Throws<SteamException>(() => SteamFreeGamesClient.ParseFreeToKeep("{}", Now));
    }

    [Fact]
    public async Task FetchAsync_GivenAwayAndWeekends_OneCardPerGameEndingFirst()
    {
        // Portal given away and on a free weekend too: one card, to keep.
        string givenAway = Query(Item(10, "Portal", FreeToKeep(Now.AddDays(5))));
        string firstPage = Query(
            Item(10, "Portal", FreeToKeep(Now.AddDays(5)), Weekend(Now.AddDays(-1), Now.AddDays(2))),
            Item(20, "Stranded Deep", weekend: Weekend(Now.AddDays(-1), Now.AddDays(3))));
        string secondPage = Query(Item(30, "Battlefield 6", weekend: Weekend(Now.AddDays(-1), Now.AddDays(1))));
        // The weekends' pictures: asked apart, for the few games found.
        string pictures = """
            { "response": { "store_items": [
              { "success": 1, "appid": 20, "name": "Stranded Deep", "assets": { "asset_url_format": "steam/apps/20/${FILENAME}", "main_capsule": "picture.jpg" } },
              { "success": 1, "appid": 30, "name": "Battlefield 6", "assets": { "asset_url_format": "steam/apps/30/${FILENAME}", "main_capsule": "picture.jpg" } } ] } }
            """;
        var pictureRequests = new List<string>();
        using var http = new HttpClient(new TestData.JsonHandler(uri =>
        {
            string input = Uri.UnescapeDataString(uri.Query);
            if (uri.AbsolutePath.Contains("GetItems"))
            {
                pictureRequests.Add(input);
                return pictures;
            }
            return input.Contains("min_discount_percent") ? givenAway : input.Contains("\"start\":0") ? firstPage : secondPage;
        }));

        var games = await new SteamFreeGamesClient(http).FetchAsync(Now);

        Assert.Equal(["Battlefield 6", "Stranded Deep", "Portal"], games.Select(g => g.Title));
        Assert.False(games.Single(g => g.Title == "Portal").PlayOnly);
        // One request for the pictures of the weekends, not for the game given away (it has its own).
        Assert.Contains("\"ids\":[{\"appid\":20},{\"appid\":30}]", Assert.Single(pictureRequests));
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/30/picture.jpg",
            games.Single(g => g.Title == "Battlefield 6").ImageUrl);
    }

    [Fact]
    public async Task FetchAsync_ThePicturesFailing_KeepsTheGames()
    {
        string page = Query(Item(30, "Battlefield 6", weekend: Weekend(Now.AddDays(-1), Now.AddDays(1))));
        using var http = new HttpClient(new TestData.JsonHandler(uri => uri.AbsolutePath.Contains("GetItems") ? "{}" : page));

        var game = Assert.Single(await new SteamFreeGamesClient(http).FetchAsync(Now));

        // Only the pictures failed: the card stays.
        Assert.Equal("Battlefield 6", game.Title);
    }

    [Fact]
    public async Task FetchAsync_TheStoreDown_Throws()
    {
        using var http = new HttpClient(new TestData.JsonHandler(_ => "", System.Net.HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<SteamException>(() => new SteamFreeGamesClient(http).FetchAsync(Now));

        // Said about the free games, not about the recommendations.
        Assert.Equal(CoreStrings.Steam_FreeGamesDown, ex.Message);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    [InlineData(System.Net.HttpStatusCode.Forbidden)]
    public async Task FetchAsync_TheStoreLimitingRequests_IsARateLimit(System.Net.HttpStatusCode status)
    {
        using var http = new HttpClient(new TestData.JsonHandler(_ => "", status));

        var ex = await Assert.ThrowsAsync<SteamException>(() => new SteamFreeGamesClient(http).FetchAsync(Now));

        Assert.True(ex.IsRateLimited);
    }

    [Fact]
    public async Task FetchAsync_ReadsTheWeekendsStartedAndNotEnded()
    {
        var end = Now.AddDays(3);
        string page = Query(
            Item(1, "Battlefield 6", weekend: Weekend(Now.AddDays(-1), end)),
            Item(2, "Next week", weekend: Weekend(Now.AddDays(6), Now.AddDays(9))),
            Item(3, "Last week", weekend: Weekend(Now.AddDays(-8), Now.AddDays(-4))),
            Item(4, "No weekend"),
            Item(5, "Adult", weekend: Weekend(Now.AddDays(-1), end), descriptors: "[4]"));
        // Nothing given away, no pictures: the weekends alone.
        using var http = new HttpClient(new TestData.JsonHandler(uri => uri.AbsolutePath.Contains("GetItems") ? "{}"
            : Uri.UnescapeDataString(uri.Query).Contains("min_discount_percent") ? Query() : page));

        var game = Assert.Single(await new SteamFreeGamesClient(http).FetchAsync(Now));

        Assert.Equal("Battlefield 6", game.Title);
        Assert.Equal(end, game.Until);
        Assert.Equal("https://store.steampowered.com/app/1/", game.Link);
        // Free to play for a while, not to keep.
        Assert.True(game.PlayOnly);
    }
}
