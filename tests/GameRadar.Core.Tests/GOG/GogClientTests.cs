using System.Net;
using System.Text;
using GameRadar.Core.GOG;
using GameRadar.Core.Resources;

namespace GameRadar.Core.Tests.GOG;

public class GogClientTests
{
    /// <summary>One game of the profile; stats null = "[]" (never followed by GOG Galaxy).</summary>
    private static string Item(string id, string title, string? stats = null) => $$"""
        { "game": { "id": "{{id}}", "title": "{{title}}", "url": "/game/x", "achievementSupport": true, "image": "//images.gog-statics.com/x" },
          "stats": {{stats ?? "[]"}} }
        """;

    private static string Page(int pages, params string[] items) =>
        $$"""{ "page": 1, "limit": 50, "pages": {{pages}}, "total": {{items.Length}}, "_embedded": { "items": [{{string.Join(",", items)}}] } }""";

    [Fact]
    public void ParseJson_ReadsPlayTimeLastSessionAndAchievements()
    {
        string stats = """{ "48628349957132247": { "playtime": 6615, "lastSession": "2026-09-30T21:15:02+00:00", "achievementsPercentage": 49 } }""";

        var page = GogClient.ParseJson(Page(3, Item("1207658930", "The Witcher 3: Wild Hunt", stats)));

        Assert.Equal(3, page.Pages);
        var game = Assert.Single(page.Games);
        Assert.Equal("1207658930", game.Id);
        Assert.Equal("The Witcher 3: Wild Hunt", game.Title);
        Assert.Equal(6615, game.PlaytimeMinutes);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 21, 15, 2, TimeSpan.Zero), game.LastSession);
        Assert.Equal(49, game.AchievementsPercentage);
        // Its picture at the card's size (as the GOG catalog's), in https.
        Assert.Equal("https://images.gog-statics.com/x_product_card_v2_mobile_slider_639.jpg", game.ImageUrl);
    }

    [Fact]
    public void ParseJson_AGameGalaxyNeverFollowedHasNoPlayTime()
    {
        // "stats": [] rather than an object: owned, played or not outside Galaxy.
        var game = Assert.Single(GogClient.ParseJson(Page(1, Item("1", "Owned Game"))).Games);

        Assert.Null(game.PlaytimeMinutes);
        Assert.Null(game.LastSession);
        Assert.Null(game.AchievementsPercentage);
    }

    [Fact]
    public void ParseJson_AGameWithoutAchievementsHasNoPercentage()
    {
        string stats = """{ "1": { "playtime": 2, "lastSession": "2026-01-02T03:04:05+01:00" } }""";

        var game = Assert.Single(GogClient.ParseJson(Page(1, Item("1", "No Achievements", stats))).Games);

        Assert.Equal(2, game.PlaytimeMinutes);
        Assert.Null(game.AchievementsPercentage);
    }

    [Fact]
    public void ParseJson_NullStatsAndPagesCountAsUnknown()
    {
        string stats = """{ "1": { "playtime": null, "lastSession": null, "achievementsPercentage": "n/a" } }""";
        string json = Page(1, Item("1", "Null Stats", stats)).Replace("\"pages\": 1", "\"pages\": null");

        var page = GogClient.ParseJson(json);

        Assert.Equal(1, page.Pages);
        var game = Assert.Single(page.Games);
        Assert.Null(game.PlaytimeMinutes);
        Assert.Null(game.LastSession);
        Assert.Null(game.AchievementsPercentage);
    }

    [Fact]
    public void ParseJson_SkipsItemsWithoutIdOrTitle()
    {
        string json = Page(1, """{ "game": { "id": "1" }, "stats": [] }""", Item("2", "Kept"));

        Assert.Equal("Kept", Assert.Single(GogClient.ParseJson(json).Games).Title);
    }

    [Theory]
    [InlineData("""{ "page": 1 }""")]
    [InlineData("[]")]
    [InlineData("<html>Maintenance</html>")]
    public void ParseJson_UnexpectedAnswer_ThrowsGogException(string json)
    {
        Assert.Throws<GogException>(() => GogClient.ParseJson(json));
    }

    [Theory]
    [InlineData("Radar_Player", "Radar_Player")]
    [InlineData("  Radar_Player  ", "Radar_Player")]
    [InlineData("https://www.gog.com/u/Radar_Player", "Radar_Player")]
    [InlineData("https://www.gog.com/u/Radar_Player/", "Radar_Player")]
    [InlineData("https://www.gog.com/fr/u/Radar_Player/games?sort=recent", "Radar_Player")]
    [InlineData("gog.com/u/Radar_Player#games", "Radar_Player")]
    [InlineData("https://www.gog.com/en/game/cyberpunk_2077", null)]
    [InlineData("https://example.com/u/Radar_Player", null)]
    [InlineData("two words", null)]
    [InlineData("", null)]
    public void UserOf_ReadsANameOrAProfileAddress(string pasted, string? user)
    {
        Assert.Equal(user, GogClient.UserOf(pasted));
    }

    [Fact]
    public async Task FetchPageJsonAsync_AsksThePublicProfilePage()
    {
        Uri? asked = null;
        var client = new GogClient(new HttpClient(new FakeHandler(HttpStatusCode.OK, Page(1), uri => asked = uri)));

        string json = await client.FetchPageJsonAsync("Radar_Player", 2);

        Assert.Equal(Page(1), json);
        Assert.Equal("https://www.gog.com/u/Radar_Player/games/stats?sort=recent_playtime&order=desc&page=2", asked!.ToString());
    }

    [Fact]
    public async Task FetchPageJsonAsync_ExplainsEachRefusal()
    {
        static async Task<string> MessageFor(HttpStatusCode status) =>
            (await Assert.ThrowsAsync<GogException>(() =>
                new GogClient(new HttpClient(new FakeHandler(status, ""))).FetchPageJsonAsync("Radar_Player", 1))).Message;

        Assert.Equal(CoreText.Format(CoreStrings.Gog_PrivateProfile, "Radar_Player"), await MessageFor(HttpStatusCode.Forbidden));
        Assert.Equal(CoreText.Format(CoreStrings.Gog_NoSuchProfile, "Radar_Player"), await MessageFor(HttpStatusCode.NotFound));
        Assert.Equal(CoreStrings.Gog_RateLimited, await MessageFor(HttpStatusCode.TooManyRequests));
        Assert.Equal(CoreStrings.Gog_ProfileDown, await MessageFor(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class FakeHandler(HttpStatusCode status, string body, Action<Uri>? asked = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            asked?.Invoke(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/hal+json")
            });
        }
    }
}
