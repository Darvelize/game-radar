using System.Text.Json;
using GameRadar.Core.Epic;
using GameRadar.Core.Models;

namespace GameRadar.Core.Tests.Epic;

public class EpicCatalogClientTests
{
    /// <summary>One offer of a store collection, with only what a test changes.</summary>
    private static string Offer(
        string ns,
        string title = "A Game",
        string categories = """[{"path":"games"},{"path":"games/edition/base"}]""",
        string tags = """[{"id":"1","name":"Action","groupName":"genre"},{"id":"2","name":"Single Player","groupName":"feature"}]""",
        string? pageSlug = "a-game") => $$"""
        { "title": "{{title}}", "id": "offer-{{ns}}", "namespace": "{{ns}}",
          "categories": {{categories}}, "tags": {{tags}},
          "keyImages": [ { "type": "OfferImageTall", "url": "https://cdn1.epicgames.com/{{ns}}-tall.jpg" },
                         { "type": "OfferImageWide", "url": "https://cdn1.epicgames.com/{{ns}}-wide.jpg" } ],
          "catalogNs": { "mappings": {{(pageSlug is null ? "[]" : $$"""[ { "pageSlug": "{{pageSlug}}" } ]""")}} } }
        """;

    private static string Collection(params string[] offers) =>
        $$"""{ "data": { "Storefront": { "collectionLayout": { "collectionOffers": [{{string.Join(",", offers)}}] } } } }""";

    [Fact]
    public void ParseCollection_ReadsAGame()
    {
        var offer = Assert.Single(EpicCatalogClient.ParseCollection(Collection(Offer("ns-aw2", "Alan Wake 2", pageSlug: "alan-wake-2"))));

        Assert.Equal("ns-aw2", offer.Namespace);
        Assert.Equal("Alan Wake 2", offer.Title);
        Assert.Equal("alan-wake-2", offer.PageSlug);
        Assert.Equal("https://cdn1.epicgames.com/ns-aw2-wide.jpg", offer.ImageUrl);
        // Already in the profile's vocabulary.
        Assert.Equal(["Action & adventure"], offer.Genres);
        Assert.Equal(["Single Player"], offer.Features);
    }

    [Fact]
    public void ParseCollection_LeavesOutAppsAddOnsAndPagelessOffers()
    {
        string json = Collection(
            // An app in "most played": no genre tag.
            Offer("ns-app", "Crosshair X", tags: """[{"id":"9","name":"Windows","groupName":"platform"}]"""),
            Offer("ns-dlc", "A DLC", categories: """[{"path":"addons"}]"""),
            Offer("ns-nopage", pageSlug: null),
            Offer("ns-ok"));

        Assert.Equal(["ns-ok"], EpicCatalogClient.ParseCollection(json).Select(o => o.Namespace));
    }

    [Theory]
    [InlineData("""{"data":{"RatingsPolls":{"getProductResult":{"averageRating":4.66}}}}""", 4.66)]
    [InlineData("""{"data":{"RatingsPolls":{"getProductResult":{"averageRating":null}}}}""", null)]
    // A little known game: Epic answers 200 with an error inside (measured).
    [InlineData("""{"errors":[{"message":"RatingsPollsQuery/getProductResult: Request failed with status code 404"}],"data":{"RatingsPolls":{"getProductResult":null}}}""", null)]
    public void ParseRating_IsNullWhenEpicHasNone(string json, double? rating)
    {
        Assert.Equal(rating, EpicCatalogClient.ParseRating(json));
    }

    [Theory]
    [InlineData("Online Co-op", PlayModes.CoopOnline)]
    [InlineData("Local Co-op", PlayModes.Local)]
    // Neither online nor local: a local-only co-op game must not pass for online.
    [InlineData("Co-op", PlayModes.None)]
    [InlineData("Multiplayer", PlayModes.None)]
    public void ModeOf_OnlySaysOnlineWhenEpicDoes(string feature, PlayModes expected)
    {
        Assert.Equal(expected, EpicCatalogClient.ModeOf(feature));
    }

    [Fact]
    public void ToCatalogGame_TranslatesTheOffer()
    {
        var offer = EpicCatalogClient.ParseCollection(Collection(Offer("ns-aw2", "Alan Wake 2", pageSlug: "alan-wake-2",
            tags: """[{"id":"1","name":"Action-Adventure","groupName":"genre"},{"id":"3","name":"Horror","groupName":"genre"},{"id":"2","name":"Single Player","groupName":"feature"},{"id":"4","name":"Online Multiplayer","groupName":"feature"}]""")))[0];

        var game = EpicCatalogClient.ToCatalogGame(offer, rating: 4.8)!;

        Assert.Equal("epic:ns-aw2", game.ProductId);
        Assert.Equal("Alan Wake 2", game.Title);
        // "Horror" is a setting, not a gameplay genre.
        Assert.Equal(["Action & adventure"], game.Genres);
        Assert.Equal(PlayModes.Solo | PlayModes.MultiOnline, game.Modes);
        Assert.Equal(4.8, game.AverageRating);
        Assert.Equal("https://store.epicgames.com/p/alan-wake-2", game.EpicStoreLink);
        // Epic gives no count: weighed so that the rating counts in the ranking.
        Assert.Equal(EpicCatalogClient.RatingWeight, game.RatingCount);
        Assert.Equal(CatalogSource.Epic, game.PrimaryStore);
        Assert.False(game.InGamePass);
    }

    [Theory]
    [InlineData(2.9, false)]
    [InlineData(3.0, true)]
    [InlineData(null, true)] // no rating: the collections already vouch for it
    public void ToCatalogGame_KeepsGamesRatedThreeOrMore(double? rating, bool kept)
    {
        var offer = EpicCatalogClient.ParseCollection(Collection(Offer("ns-1")))[0];

        Assert.Equal(kept, EpicCatalogClient.ToCatalogGame(offer, rating) is not null);
    }

    [Fact]
    public void ParseCollection_AnOfferWithoutAGenreTheProfileKnows_IsLeftOutBeforeItsRating()
    {
        // "Horror" is a setting: no rating is asked for a game that would be dropped.
        Assert.Empty(EpicCatalogClient.ParseCollection(Collection(Offer("ns-1",
            tags: """[{"id":"3","name":"Horror","groupName":"genre"}]"""))));
    }

    [Fact]
    public void ACatalogWithoutEpicLinks_IsReadAsBefore()
    {
        // A cached catalog of 1.5.x: no EpicStoreLink written.
        var game = JsonSerializer.Deserialize("""{"ProductId":"gog:1","Title":"T","XboxTitleId":null,"PackageFamilyName":null,"Genres":["Strategy"],"OnPc":false,"OnConsole":false,"LeavingSoon":false,"GogStoreLink":"https://www.gog.com/en/game/t"}""",
            CoreJsonContext.Default.CatalogGame)!;

        Assert.Null(game.EpicStoreLink);
        Assert.Equal(CatalogSource.Gog, game.PrimaryStore);
        // Not written when null: the cache format stays as it was.
        Assert.DoesNotContain("EpicStoreLink", JsonSerializer.Serialize(game, CoreJsonContext.Default.CatalogGame));
    }
}
