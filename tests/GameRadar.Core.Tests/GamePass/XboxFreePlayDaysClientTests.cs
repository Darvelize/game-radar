using System.Net;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;

namespace GameRadar.Core.Tests.GamePass;

public class XboxFreePlayDaysClientTests
{
    // Thursday 22 October 2026, 18:00 UTC: this week's Free Play Days run until Sunday 25.
    private static readonly DateTimeOffset Thursday = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ThisSunday = new(2026, 10, 25, 23, 59, 59, TimeSpan.FromHours(-7));

    /// <summary>One product summary of the browse answer.</summary>
    private static string Product(string id, string title, string kind = "Game", string? hero = "hero", string? box = "box") => $$"""
        { "productId": "{{id}}", "title": "{{title}}", "productKind": "{{kind}}",
          "images": { {{string.Join(", ", new[] {
              hero is null ? null : $$"""  "superHeroArt": { "url": "https://store-images.s-microsoft.com/image/{{id}}-{{hero}}", "width": 3840, "height": 2160 }""",
              box is null ? null : $$"""  "boxArt": { "url": "https://store-images.s-microsoft.com/image/{{id}}-{{box}}", "width": 2160, "height": 2160 }""" }
              .Where(s => s is not null))}} } }
        """;

    /// <summary>The browse answer: the channel's order, then the summaries in any order.</summary>
    private static string Browse(string[] order, params string[] products) => $$"""
        { "channels": { "BROWSE_CHANNELID=FREE-PLAY-DAYS_FILTERS=": { "products": [ {{string.Join(",", order.Select(id => $$"""{ "productId": "{{id}}" }"""))}} ] } },
          "productSummaries": [ {{string.Join(",", products)}} ] }
        """;

    [Fact]
    public void ParseJson_ReadsTheGamesInTheChannelsOrder()
    {
        string json = Browse(["9MZ8RZSD0NFQ", "9N0P25WJHM8C", "CFQ7TTC0KHS0"],
            Product("9N0P25WJHM8C", "The Alters"),
            // The Game Pass plans the page shows next to the games.
            Product("CFQ7TTC0KHS0", "Xbox Game Pass Ultimate", kind: "PASS"),
            Product("9MZ8RZSD0NFQ", "Crusader Kings III"));

        var games = XboxFreePlayDaysClient.ParseJson(json, ThisSunday);

        Assert.Equal(["Crusader Kings III", "The Alters"], games.Select(g => g.Title));
        var first = games[0];
        Assert.Equal(ThisSunday, first.Until);
        // Without language: the page adds the app's, like the cards of "Pour toi".
        Assert.Equal("https://www.xbox.com/games/store/-/9MZ8RZSD0NFQ", first.Link);
        Assert.Equal("https://store-images.s-microsoft.com/image/9MZ8RZSD0NFQ-hero", first.ImageUrl);
        Assert.True(first.PlayOnly);
        Assert.Null(first.RedeemOn);
    }

    [Fact]
    public void ParseJson_TheBoxArtWithoutAWidePicture()
    {
        string json = Browse(["A", "B"], Product("A", "Square", hero: null), Product("B", "None", hero: null, box: null));

        var games = XboxFreePlayDaysClient.ParseJson(json, ThisSunday);

        Assert.Equal("https://store-images.s-microsoft.com/image/A-box", games[0].ImageUrl);
        Assert.Null(games[1].ImageUrl);
    }

    [Fact]
    public void ParseJson_LeavesOutAProductWithoutTitleOrSummary()
    {
        string json = Browse(["A", "B", "C"], Product("A", " "), Product("C", "Ravenswatch"));

        Assert.Equal(["Ravenswatch"], XboxFreePlayDaysClient.ParseJson(json, ThisSunday).Select(g => g.Title));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "channels": {} }""")]
    [InlineData("not json")]
    public void ParseJson_WithoutTheChannel_IsAnError(string json)
    {
        Assert.Throws<GamePassException>(() => XboxFreePlayDaysClient.ParseJson(json, ThisSunday));
    }

    [Theory]
    // Thursday to Sunday: this Sunday 23:59:59 Pacific (summer time: UTC-7).
    [InlineData("2026-10-22T18:00:00Z", "2026-10-25T23:59:59-07:00")]
    [InlineData("2026-10-24T12:00:00Z", "2026-10-25T23:59:59-07:00")]
    [InlineData("2026-10-26T06:00:00Z", "2026-10-25T23:59:59-07:00")] // Sunday 23:00 in Seattle
    // Monday to Wednesday: last Sunday, already over.
    [InlineData("2026-10-19T10:00:00Z", "2026-10-18T23:59:59-07:00")]
    [InlineData("2026-10-22T06:00:00Z", "2026-10-18T23:59:59-07:00")] // Wednesday 23:00 in Seattle
    // The Sunday summer time ends: winter time (UTC-8).
    [InlineData("2026-10-30T12:00:00Z", "2026-11-01T23:59:59-08:00")]
    public void FreePlayDaysEnd_TheSundayOfTheWeekFromThursday(string now, string end)
    {
        Assert.Equal(DateTimeOffset.Parse(end), XboxFreePlayDaysClient.FreePlayDaysEnd(DateTimeOffset.Parse(now)));
    }

    /// <summary>A fake xbox.com that keeps the request it was sent.</summary>
    private sealed class BrowseServer(string answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return TestData.Json(answer, status);
        }
    }

    [Fact]
    public async Task FetchAsync_PostsTheFreePlayDaysChannelInEnglish()
    {
        var server = new BrowseServer(Browse(["A"], Product("A", "Soccer Story")));
        using var http = new HttpClient(server);

        var games = await new XboxFreePlayDaysClient(http).FetchAsync(Thursday, previous: null);

        Assert.Equal(ThisSunday, Assert.Single(games).Until);
        var request = server.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("emerald.xboxservices.com", request.RequestUri!.Host);
        Assert.Contains("locale=en-US", request.RequestUri.Query);
        Assert.Equal("1.1", Assert.Single(request.Headers.GetValues("x-ms-api-version")));
        // Any correlation vector will do, but without one the API answers 400.
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(request.Headers.GetValues("MS-CV"))));
        Assert.Contains("\"ChannelId\":\"FREE-PLAY-DAYS\"", server.Body);
        Assert.Contains("\"ChannelKeyToBeUsedInResponse\":\"BROWSE_CHANNELID=FREE-PLAY-DAYS_FILTERS=\"", server.Body);
    }

    [Fact]
    public async Task FetchAsync_TheSameEndedList_KeepsItsEnd()
    {
        // Thursday morning, before xbox.com's update: last weekend's games, still online.
        var lastSunday = new DateTimeOffset(2026, 10, 18, 23, 59, 59, TimeSpan.FromHours(-7));
        var previous = new FreeGameList(
            [new FreeGame("Soccer Story", "https://www.xbox.com/games/store/-/A", lastSunday, PlayOnly: true)], Thursday.AddDays(-3));
        using var http = new HttpClient(new BrowseServer(Browse(["A"], Product("A", "Soccer Story"))));

        var games = await new XboxFreePlayDaysClient(http).FetchAsync(Thursday, previous);

        Assert.Equal(lastSunday, Assert.Single(games).Until);
    }

    [Fact]
    public async Task FetchAsync_TheSameEndedListInAnotherOrder_KeepsItsEnd()
    {
        var lastSunday = new DateTimeOffset(2026, 10, 18, 23, 59, 59, TimeSpan.FromHours(-7));
        var previous = new FreeGameList(
            [new FreeGame("Soccer Story", "https://www.xbox.com/games/store/-/A", lastSunday, PlayOnly: true),
             new FreeGame("Ravenswatch", "https://www.xbox.com/games/store/-/B", lastSunday, PlayOnly: true)], Thursday.AddDays(-3));
        using var http = new HttpClient(new BrowseServer(Browse(["B", "A"], Product("A", "Soccer Story"), Product("B", "Ravenswatch"))));

        var games = await new XboxFreePlayDaysClient(http).FetchAsync(Thursday, previous);

        Assert.All(games, g => Assert.Equal(lastSunday, g.Until));
    }

    [Fact]
    public async Task FetchAsync_ANewList_EndsThisSunday()
    {
        var lastSunday = new DateTimeOffset(2026, 10, 18, 23, 59, 59, TimeSpan.FromHours(-7));
        var previous = new FreeGameList(
            [new FreeGame("Soccer Story", "https://www.xbox.com/games/store/-/A", lastSunday, PlayOnly: true)], Thursday.AddDays(-3));
        using var http = new HttpClient(new BrowseServer(Browse(["A", "B"], Product("A", "Soccer Story"), Product("B", "Ravenswatch"))));

        var games = await new XboxFreePlayDaysClient(http).FetchAsync(Thursday, previous);

        Assert.All(games, g => Assert.Equal(ThisSunday, g.Until));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    public async Task FetchAsync_AnHttpError_IsAGamePassError(HttpStatusCode status, bool rateLimited)
    {
        using var http = new HttpClient(new BrowseServer("", status));

        var error = await Assert.ThrowsAsync<GamePassException>(() => new XboxFreePlayDaysClient(http).FetchAsync(Thursday, previous: null));

        Assert.Equal(rateLimited, error.IsRateLimited);
    }
}
