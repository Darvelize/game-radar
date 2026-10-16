using System.Net;
using GameRadar.Core.Epic;

namespace GameRadar.Core.Tests.Epic;

public class EpicFreeGamesClientTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    /// <summary>One element of the promotions answer, with only what a test changes.</summary>
    private static string Element(
        string title,
        int discountPrice = 0,
        int originalPrice = 1999,
        string? current = null,
        string? upcoming = null,
        string offerType = "BASE_GAME",
        string? pageSlug = "a-game",
        string? productSlug = null,
        string? offerSlug = null) => $$"""
        { "title": "{{title}}", "offerType": "{{offerType}}",
          "productSlug": {{(productSlug is null ? "null" : $"\"{productSlug}\"")}},
          "keyImages": [ { "type": "OfferImageTall", "url": "https://cdn1.epicgames.com/{{title}}-tall.jpg" },
                         { "type": "OfferImageWide", "url": "https://cdn1.epicgames.com/{{title}}-wide.jpg" } ],
          "catalogNs": { "mappings": {{(pageSlug is null ? "[]" : $$"""[ { "pageSlug": "{{pageSlug}}" } ]""")}} },
          "offerMappings": {{(offerSlug is null ? "null" : $$"""[ { "pageSlug": "{{offerSlug}}" } ]""")}},
          "price": { "totalPrice": { "discountPrice": {{discountPrice}}, "originalPrice": {{originalPrice}} } },
          "promotions": { "promotionalOffers": [ { "promotionalOffers": [ {{current}} ] } ],
                          "upcomingPromotionalOffers": [ { "promotionalOffers": [ {{upcoming}} ] } ] } }
        """;

    private static string Offer(string start, string end, int percentage = 0) =>
        $$"""{ "startDate": "{{start}}", "endDate": "{{end}}", "discountSetting": { "discountType": "PERCENTAGE", "discountPercentage": {{percentage}} } }""";

    // TestData.Now is 2026-09-23 12:00 UTC: this week's offer runs around it.
    private static readonly string ThisWeek = Offer("2026-09-17T15:00:00.000Z", "2026-09-24T15:00:00.000Z");

    private static string Promotions(params string[] elements) =>
        $$"""{ "data": { "Catalog": { "searchStore": { "elements": [{{string.Join(",", elements)}}] } } } }""";

    [Fact]
    public void ParseJson_ReadsAGameFreeThisWeek()
    {
        var game = Assert.Single(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Out of Sight", current: ThisWeek, pageSlug: "out-of-sight-b96ca8")), Now));

        Assert.Equal("Out of Sight", game.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 15, 0, 0, TimeSpan.Zero), game.Until);
        // Without language: the page adds the app's, like the cards of "Pour toi".
        Assert.Equal("https://store.epicgames.com/p/out-of-sight-b96ca8", game.Link);
        Assert.Equal("https://cdn1.epicgames.com/Out of Sight-wide.jpg", game.ImageUrl);
        Assert.Null(game.RedeemOn);
    }

    [Fact]
    public void ParseJson_KeepsOnlyWhatIsFreeNow()
    {
        string json = Promotions(
            // Featured, no offer at all.
            Element("Featured", discountPrice: 1999, current: null),
            // A sale, not a gift.
            Element("On sale", discountPrice: 999, current: Offer("2026-09-17T15:00:00.000Z", "2026-09-24T15:00:00.000Z", percentage: 50)),
            // Free next week only.
            Element("Next week", discountPrice: 1999, upcoming: Offer("2026-09-24T15:00:00.000Z", "2026-10-01T15:00:00.000Z")),
            // Ended (the answer can lag behind).
            Element("Ended", current: Offer("2026-09-10T15:00:00.000Z", "2026-09-17T15:00:00.000Z")),
            Element("A DLC", offerType: "ADD_ON", current: ThisWeek),
            Element("Free", current: ThisWeek));

        Assert.Equal(["Free"], EpicFreeGamesClient.ParseJson(json, Now).Select(g => g.Title));
    }

    [Fact]
    public void ParseJson_UsesTheProductSlugWhenThereIsNoPageMapping()
    {
        var game = Assert.Single(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Ghostrunner 2", current: ThisWeek, pageSlug: null, productSlug: "ghostrunner-2")), Now));

        Assert.Equal("https://store.epicgames.com/p/ghostrunner-2", game.Link);
    }

    [Fact]
    public void ParseJson_UsesTheOfferMappingWhenTheCatalogHasNone()
    {
        var game = Assert.Single(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Hue", current: ThisWeek, pageSlug: null, productSlug: null, offerSlug: "hue-4c3e8a")), Now));

        Assert.Equal("https://store.epicgames.com/p/hue-4c3e8a", game.Link);
    }

    [Fact]
    public void ParseJson_AnEmptyPageMappingFallsBackToTheNextOne()
    {
        var game = Assert.Single(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Hue", current: ThisWeek, pageSlug: "", offerSlug: "hue-4c3e8a")), Now));

        Assert.Equal("https://store.epicgames.com/p/hue-4c3e8a", game.Link);
    }

    [Fact]
    public void ParseJson_AProductSlugKeepsOnlyTheGamesPart()
    {
        // Measured: "thems-fightin-herds/home"; the page is store.epicgames.com/p/thems-fightin-herds.
        var game = Assert.Single(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Them's Fightin' Herds", current: ThisWeek, pageSlug: null, productSlug: "thems-fightin-herds/home")), Now));

        Assert.Equal("https://store.epicgames.com/p/thems-fightin-herds", game.Link);
    }

    [Fact]
    public void ParseJson_EscapesTheSlug()
    {
        var game = Assert.Single(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Odd", current: ThisWeek, pageSlug: "odd game?x")), Now));

        Assert.Equal("https://store.epicgames.com/p/odd%20game%3Fx", game.Link);
    }

    [Fact]
    public void ParseJson_LeavesOutAGameWithoutAPage()
    {
        Assert.Empty(EpicFreeGamesClient.ParseJson(
            Promotions(Element("Mystery Game", current: ThisWeek, pageSlug: null, productSlug: null)), Now));
    }

    [Fact]
    public void ParseJson_SortsByTheEndOfTheOffer()
    {
        string json = Promotions(
            Element("Later", current: Offer("2026-09-17T15:00:00.000Z", "2026-10-01T15:00:00.000Z")),
            Element("Sooner", current: ThisWeek));

        Assert.Equal(["Sooner", "Later"], EpicFreeGamesClient.ParseJson(json, Now).Select(g => g.Title));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "data": null }""")]
    public void ParseJson_AnUnexpectedAnswer_IsAnEpicError(string json)
    {
        Assert.Throws<EpicException>(() => EpicFreeGamesClient.ParseJson(json, Now));
    }

    [Fact]
    public async Task FetchAsync_AsksTheFrenchMarketInEnglish()
    {
        Uri? asked = null;
        using var http = new HttpClient(new TestData.JsonHandler(uri =>
        {
            asked = uri;
            return Promotions(Element("Free", current: ThisWeek));
        }));

        var games = await new EpicFreeGamesClient(http).FetchAsync(Now);

        Assert.Single(games);
        Assert.Equal("store-site-backend-static.ak.epicgames.com", asked!.Host);
        Assert.Contains("locale=en-US", asked.Query);
        Assert.Contains("country=FR", asked.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task FetchAsync_AnHttpError_IsAnEpicError(HttpStatusCode status, bool rateLimited)
    {
        using var http = new HttpClient(new TestData.JsonHandler(_ => "", status));

        var error = await Assert.ThrowsAsync<EpicException>(() => new EpicFreeGamesClient(http).FetchAsync(Now));

        Assert.Equal(rateLimited, error.IsRateLimited);
    }
}
