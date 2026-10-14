using System.Net;
using GameRadar.Core.PlayStation;

namespace GameRadar.Core.Tests.PlayStation;

/// <summary>The PlayStation Store best sellers: the category grid and the product details, without any account.</summary>
public class PlayStationStoreCatalogTests
{
    // Shape of the real answer (categoryGridRetrieve, "All PS5 games", sales30, en-GB).
    private const string GridJson = """
        { "data": { "categoryGridRetrieve": { "__typename": "CategoryGrid", "concepts": null,
          "pageInfo": { "totalCount": 7079, "offset": 0, "size": 3, "isLast": false },
          "products": [
            { "__typename": "Product", "id": "EP9000-PPSA21567_00-0000000000000000", "name": "ASTRO BOT™", "npTitleId": "PPSA21567_00",
              "platforms": ["PS5"], "storeDisplayClassification": "FULL_GAME",
              "media": [ { "type": "IMAGE", "role": "MASTER", "url": "https://image.api.playstation.com/master.png" },
                         { "type": "VIDEO", "role": "PREVIEW", "url": "https://gs2-sec.ww.prod.dl.playstation.net/preview.mp4" },
                         { "type": "IMAGE", "role": "SIXTEEN_BY_NINE_BANNER", "url": "https://image.api.playstation.com/banner.jpg" } ] },
            { "__typename": "Product", "id": "EP0082-PPSA37334_00-0780452152443749", "name": "FINAL FANTASY VII Remake Series Trilogy Edition",
              "platforms": ["PS5"], "storeDisplayClassification": "GAME_BUNDLE", "media": [] },
            { "__typename": "Product", "id": "EP1004-CUSA08519_00-REDEMPTION000002", "name": " Red Dead Redemption 2 ",
              "platforms": ["PS4"], "storeDisplayClassification": "FULL_GAME",
              "media": [ { "type": "IMAGE", "role": "MASTER", "url": "https://image.api.playstation.com/rdr2.png" } ] },
            { "__typename": "Product", "id": "EP1004-CUSA00000_00-NONAME0000000000", "name": "", "storeDisplayClassification": "FULL_GAME" },
            { "__typename": "Product", "id": "EP1004-CUSA00001_00-NOMEDIA000000000", "name": "Tetris Effect", "storeDisplayClassification": "FULL_GAME" }
          ] } } }
        """;

    // ---------- Grid ----------

    [Fact]
    public void The_grid_keeps_the_full_games_in_sales_order()
    {
        var games = PlayStationStoreClient.ParseGrid(GridJson);

        // Not the bundle, nor the nameless product; names trimmed.
        Assert.Equal(["ASTRO BOT™", "Red Dead Redemption 2", "Tetris Effect"], games.Select(g => g.Title));
        Assert.Equal("EP9000-PPSA21567_00-0000000000000000", games[0].ProductId);
    }

    [Fact]
    public void The_card_picture_is_the_wide_banner_then_the_master_art()
    {
        var games = PlayStationStoreClient.ParseGrid(GridJson);

        Assert.Equal("https://image.api.playstation.com/banner.jpg", games[0].ImageUrl);
        Assert.Equal("https://image.api.playstation.com/rdr2.png", games[1].ImageUrl);
        Assert.Null(games[2].ImageUrl);
    }

    [Theory]
    [InlineData("""{ "errors": [ { "message": "PersistedQueryNotFound" } ] }""")]
    [InlineData("""{ "data": { "categoryGridRetrieve": null } }""")]
    public void A_grid_without_data_means_the_site_changed(string json)
    {
        // Not an empty list: the persisted query is refused or the category is gone.
        Assert.Throws<PlayStationException>(() => PlayStationStoreClient.ParseGrid(json));
    }

    // ---------- Product details ----------

    [Fact]
    public void The_details_give_the_genres_and_the_players_rating()
    {
        var details = PlayStationStoreClient.ParseDetails("""
            { "data": { "productRetrieve": { "name": "Baldur's Gate 3",
              "localizedGenres": [ { "value": "Role Playing Games" } ],
              "starRating": { "__typename": "StarRating", "averageRating": 4.78 } } } }
            """);

        Assert.Equal(["Role playing"], details.Genres);
        Assert.Equal(4.78, details.Rating);
    }

    [Theory]
    [InlineData("""{ "data": { "productRetrieve": { "starRating": null } } }""")]
    [InlineData("""{ "data": { "productRetrieve": { "starRating": { "averageRating": null } } } }""")]
    [InlineData("""{ "data": { "productRetrieve": {} } }""")]
    // What Sony gives a game nobody rated yet (pre-orders, measured on 12/10/2026): not a 0/5.
    [InlineData("""{ "data": { "productRetrieve": { "starRating": { "averageRating": 0 } } } }""")]
    public void A_game_without_rating_has_none(string json)
    {
        Assert.Null(PlayStationStoreClient.ParseDetails(json).Rating);
    }

    [Theory]
    [InlineData("Sport", "Sports")]
    [InlineData("Driving/Racing", "Racing & flying")]
    [InlineData("Music/Rhythm", "Music")]
    public void The_store_genre_names_are_translated(string storeGenre, string expected)
    {
        var details = PlayStationStoreClient.ParseDetails(
            $$"""{ "data": { "productRetrieve": { "localizedGenres": [ { "value": "{{storeGenre}}" } ] } } }""");

        Assert.Equal([expected], details.Genres);
    }

    // ---------- HTTP ----------

    [Fact]
    public async Task The_best_sellers_are_asked_by_sales_and_full_games_only()
    {
        var handler = new PlayStationFakeStore(HttpStatusCode.OK, GridJson);

        var games = await new PlayStationStoreClient(new HttpClient(handler))
            .FetchGridAsync(PlayStationStoreClient.Ps5Games, preOrdersOnly: false);

        Assert.Equal(3, games.Count);
        string url = Uri.UnescapeDataString(handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Contains("operationName=categoryGridRetrieve", url);
        Assert.Contains(PlayStationStoreClient.Ps5Games, url);
        Assert.Contains("\"sales30\"", url);
        Assert.Contains("\"size\":300", url);
        Assert.Contains("storeDisplayClassification:FULL_GAME", url);
        Assert.DoesNotContain("future_release_date", url);
        // No token of the player: the catalog is read without any account.
        Assert.Null(handler.Request.Headers.Authorization);
        Assert.False(handler.Request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task The_pre_orders_are_asked_by_their_filter()
    {
        var handler = new PlayStationFakeStore(HttpStatusCode.OK, GridJson);

        await new PlayStationStoreClient(new HttpClient(handler))
            .FetchGridAsync(PlayStationStoreClient.Ps4Games, preOrdersOnly: true);

        string url = Uri.UnescapeDataString(handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Contains(PlayStationStoreClient.Ps4Games, url);
        Assert.Contains("productReleaseDate:future_release_date", url);
    }

    [Fact]
    public async Task The_details_ask_for_one_product()
    {
        var handler = new PlayStationFakeStore(HttpStatusCode.OK, """{ "data": { "productRetrieve": { "starRating": { "averageRating": 3.5 } } } }""");

        var details = await new PlayStationStoreClient(new HttpClient(handler)).FetchDetailsAsync("EP9000-PPSA21567_00-0000000000000000");

        Assert.Equal(3.5, details.Rating);
        string url = Uri.UnescapeDataString(handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Contains("operationName=queryRetrieveTelemetryDataPDPProduct", url);
        Assert.Contains("EP9000-PPSA21567_00-0000000000000000", url);
    }
}
