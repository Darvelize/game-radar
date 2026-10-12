using GameRadar.Core.GOG;
using GameRadar.Core.Models;

namespace GameRadar.Core.Tests.GOG;

public class GogCatalogClientTests
{
    /// <summary>One catalog product, with only what a test changes.</summary>
    private static string Product(
        string id,
        string title = "A Game",
        string type = "game",
        int rating = 45,
        int reviews = 1_000,
        string genres = """[{"name":"Shooter","slug":"shooter"},{"name":"Role-playing","slug":"rpg"}]""",
        string tags = """[{"name":"FPS","slug":"fps"}]""",
        string features = """[{"name":"Single-player","slug":"single"}]""") => $$"""
        { "id": "{{id}}", "slug": "a_game", "productType": "{{type}}", "title": "{{title}}",
          "genres": {{genres}}, "tags": {{tags}}, "features": {{features}},
          "reviewsRating": {{rating}}, "reviewsCount": {{reviews}},
          "coverHorizontal": "https://images.gog-statics.com/{{id}}.png",
          "storeLink": "https://www.gog.com/en/game/a_game" }
        """;

    private static string Page(params string[] products) =>
        $$"""{ "pages": 83, "productCount": 8235, "products": [{{string.Join(",", products)}}] }""";

    [Fact]
    public void ParseJson_TranslatesAGame()
    {
        var game = Assert.Single(GogCatalogClient.ParseJson(Page(Product("1207658787", "Cyberpunk 2077"))));

        Assert.Equal("gog:1207658787", game.ProductId);
        Assert.Equal("Cyberpunk 2077", game.Title);
        Assert.Equal("https://www.gog.com/en/game/a_game", game.GogStoreLink);
        // GOG's genres first, in their order: "Role-playing" is GOG's word for RPG.
        Assert.Equal(["Shooter", "Role playing"], game.Genres);
        Assert.Equal(PlayModes.Solo, game.Modes);
        // 45 / 50 = 90 % positive = 4.5 / 5, the scale of the other catalogs.
        Assert.Equal(4.5, game.AverageRating);
        Assert.Equal(1_000, game.RatingCount);
        // The cover is a ~2 MB PNG: GOG's own JPEG at the card's size instead (639 × 361, ~50 KB).
        Assert.Equal("https://images.gog-statics.com/1207658787_product_card_v2_mobile_slider_639.jpg", game.ImageUrl);
        Assert.Null(game.SteamAppId);
        Assert.False(game.InGamePass);
        Assert.NotEqual(CatalogSource.Steam, game.PrimaryStore);
        Assert.Equal(CatalogSource.Gog, game.PrimaryStore);
    }

    [Fact]
    public void ParseJson_FillsGenresFromTheTags()
    {
        // Neither "Horror" nor "Fantasy" is a gameplay genre; the tags tell more.
        string genres = """[{"name":"Horror","slug":"horror"},{"name":"Fantasy","slug":"fantasy"}]""";
        string tags = """[{"name":"Atmospheric","slug":"a"},{"name":"Survival Horror","slug":"b"},{"name":"Puzzle","slug":"c"}]""";

        var game = Assert.Single(GogCatalogClient.ParseJson(Page(Product("1", genres: genres, tags: tags))));

        Assert.Equal(["Action & adventure", "Puzzle & trivia"], game.Genres);
    }

    [Fact]
    public void ParseJson_KeepsThreeGenresAtMost()
    {
        string genres = """[{"name":"Action","slug":"a"},{"name":"Strategy","slug":"b"},{"name":"Racing","slug":"c"},{"name":"Puzzle","slug":"d"}]""";

        var game = Assert.Single(GogCatalogClient.ParseJson(Page(Product("1", genres: genres))));

        Assert.Equal(["Action & adventure", "Strategy", "Racing & flying"], game.Genres);
    }

    [Theory]
    [InlineData("Role-playing", "Role playing")]
    [InlineData("Point-and-click", "Action & adventure")]
    [InlineData("Managerial", "Simulation")]
    [InlineData("Rally", "Racing & flying")]
    [InlineData("Soccer", "Sports")]
    [InlineData("FPS", "Shooter")]          // a Steam tag name: Steam's table
    [InlineData("Turn-based", null)]        // how, not what
    [InlineData("Sci-fi", null)]            // a setting
    public void GenreOf_TranslatesGogWordsAndSteamTags(string name, string? genre)
    {
        Assert.Equal(genre, GogCatalogClient.GenreOf(name));
    }

    [Fact]
    public void ParseJson_ReadsLocalPlayFromTheTags()
    {
        string tags = """[{"name":"Local Co-Op","slug":"a"},{"name":"FPS","slug":"b"}]""";
        string features = """[{"name":"Single-player","slug":"single"},{"name":"Multi-player","slug":"multi"}]""";

        var game = Assert.Single(GogCatalogClient.ParseJson(Page(Product("1", tags: tags, features: features))));

        // "Multi-player" says neither online nor local: not guessed.
        Assert.Equal(PlayModes.Solo | PlayModes.Local, game.Modes);
    }

    [Theory]
    [InlineData(1_000, 34)] // below 70 % positive
    [InlineData(99, 50)]    // too little known
    public void ParseJson_SkipsLittleKnownOrPoorlyRatedGames(int reviews, int rating)
    {
        Assert.Empty(GogCatalogClient.ParseJson(Page(Product("1", reviews: reviews, rating: rating))));
    }

    [Fact]
    public void ParseJson_SkipsPacksAdultGamesAndGamesWithoutGenre()
    {
        string json = Page(
            Product("1", type: "pack"),
            Product("2", tags: """[{"name":"Sexual Content","slug":"x"}]"""),
            Product("3", genres: """[{"name":"Fantasy","slug":"f"}]""", tags: """[{"name":"Classic","slug":"c"}]"""),
            Product("4"));

        var game = Assert.Single(GogCatalogClient.ParseJson(json));

        Assert.Equal("gog:4", game.ProductId);
    }

    [Fact]
    public void ParseJson_SkipsProductsWithMissingFields()
    {
        string json = Page(
            """{ "id": "1", "productType": "game", "reviewsRating": 45, "reviewsCount": 1000 }""",
            Product("2"));

        Assert.Equal("gog:2", Assert.Single(GogCatalogClient.ParseJson(json)).ProductId);
    }

    [Fact]
    public void ParseJson_AGameWithoutReviews_IsSkippedAlone()
    {
        // A new release: null rating and count. Only that game is dropped, not the page.
        string json = Page(Product("1").Replace("\"reviewsRating\": 45", "\"reviewsRating\": null"), Product("2"));

        Assert.Equal("gog:2", Assert.Single(GogCatalogClient.ParseJson(json)).ProductId);
    }

    [Theory]
    [InlineData("""{ "pages": 0 }""")]
    [InlineData("[]")]
    public void ParseJson_UnexpectedShape_Throws(string json)
    {
        Assert.Throws<GogException>(() => GogCatalogClient.ParseJson(json));
    }
}
