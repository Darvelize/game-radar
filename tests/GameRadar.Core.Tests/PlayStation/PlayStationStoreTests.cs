using System.Net;
using System.Text.Json;
using GameRadar.Core.PlayStation;

namespace GameRadar.Core.Tests.PlayStation;

/// <summary>The PlayStation Store search of "Ajouter un jeu": no account, persisted queries of the Store site.</summary>
public class PlayStationStoreTests
{
    // Shape of the real answer (getSearchResults, en-GB): games, editions, add-ons, a concept without product.
    private const string SearchJson = """
        { "data": { "universalSearch": { "__typename": "UniversalSearchResponse",
          "pageInfo": { "totalCount": 6, "isLast": true },
          "results": [
            { "__typename": "Product", "id": "EP9000-PPSA21567_00-0000000000000000", "name": "ASTRO BOT™", "storeDisplayClassification": "FULL_GAME", "platforms": ["PS5"] },
            { "__typename": "Product", "id": "EP9000-PPSA21567_00-DDE00000", "name": "ASTRO BOT Digital Deluxe Edition", "storeDisplayClassification": "PREMIUM_EDITION", "platforms": ["PS5"] },
            { "__typename": "Product", "id": "EP9000-CUSA13560_00-PFVRDEMO", "name": "ASTRO BOT Rescue Mission™ Demo", "storeDisplayClassification": "DEMO", "platforms": ["PS4"] },
            { "__typename": "Product", "id": "EP9000-CUSA12392_00-PLATFORM", "name": "ASTRO BOT Rescue Mission™", "storeDisplayClassification": "FULL_GAME", "platforms": ["PS4"] },
            { "__typename": "Concept", "id": "10019895", "name": "Astro Burn", "products": [] },
            { "__typename": "Product", "id": "EP9000-PPSA08330_00-GOWRAGNA", "name": "", "storeDisplayClassification": "FULL_GAME", "platforms": ["PS5"] }
          ] } } }
        """;

    // ---------- Search parsing ----------

    [Fact]
    public void Search_keeps_the_full_games_only()
    {
        var games = PlayStationStoreClient.ParseSearch(SearchJson);

        // Not the edition, the demo, the concept without product, nor the nameless one.
        Assert.Equal(["ASTRO BOT™", "ASTRO BOT Rescue Mission™"], games.Select(g => g.Title));
        Assert.Equal("EP9000-PPSA21567_00-0000000000000000", games[0].ProductId);
    }

    [Theory]
    [InlineData("""{ "data": { "universalSearch": { "results": [] } } }""")]
    [InlineData("""{ "data": { "universalSearch": {} } }""")]
    public void An_empty_search_has_no_game(string json)
    {
        Assert.Empty(PlayStationStoreClient.ParseSearch(json));
    }

    [Theory]
    [InlineData("""{ "errors": [ { "message": "PersistedQueryNotFound" } ] }""")]
    [InlineData("""{ "data": null }""")]
    [InlineData("{}")]
    public void An_answer_without_data_means_the_site_changed(string json)
    {
        // Not an empty result: the persisted query is refused or gone.
        Assert.Throws<PlayStationException>(() => PlayStationStoreClient.ParseSearch(json));
    }

    [Fact]
    public void Invalid_json_is_an_unexpected_format()
    {
        Assert.Throws<PlayStationException>(() => PlayStationStoreClient.ParseSearch("<html>maintenance</html>"));
    }

    // ---------- Genres ----------

    [Fact]
    public void Genres_are_brought_back_to_the_common_vocabulary()
    {
        // The Store writes "Role Playing Games"; the engine speaks the Microsoft Store's.
        var genres = PlayStationStoreClient.ParseDetails("""
            { "data": { "productRetrieve": { "name": "Bloodborne™",
              "localizedGenres": [ { "value": "Action" }, { "value": "Role Playing Games" }, { "value": "Unique" } ] } } }
            """).Genres;

        Assert.Equal(["Action & adventure", "Role playing"], genres);
    }

    [Theory]
    [InlineData("""{ "data": { "productRetrieve": { "localizedGenres": [] } } }""")]
    [InlineData("""{ "data": { "productRetrieve": {} } }""")]
    public void A_game_without_genres_gives_none(string json)
    {
        Assert.Empty(PlayStationStoreClient.ParseDetails(json).Genres);
    }

    [Fact]
    public void Genres_answered_without_data_mean_the_site_changed()
    {
        Assert.Throws<PlayStationException>(() => PlayStationStoreClient.ParseDetails("""{ "errors": [ { "message": "x" } ] }""").Genres);
    }

    // ---------- HTTP ----------

    [Fact]
    public async Task The_search_sends_the_persisted_query_without_any_account()
    {
        var handler = new FakeStore(HttpStatusCode.OK, SearchJson);

        var games = await new PlayStationStoreClient(new HttpClient(handler)).SearchGamesAsync("astro bot");

        Assert.Equal(2, games.Count);
        string url = handler.Request!.RequestUri!.AbsoluteUri;
        Assert.Contains("operationName=getSearchResults", url);
        Assert.Contains("astro%20bot", url);
        Assert.Contains("persistedQuery", Uri.UnescapeDataString(url));
        Assert.Equal("en-GB", handler.Request.Headers.GetValues("x-psn-store-locale-override").Single());
        // No token of the player: the NPSSO is never sent here.
        // The site answers 400 to a request without it, even a GET (found on the real site, not in a fake).
        Assert.Equal("application/json", handler.ContentType);
        Assert.Null(handler.Request.Headers.Authorization);
        Assert.False(handler.Request.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task The_genres_ask_for_the_chosen_product_only()
    {
        var handler = new FakeStore(HttpStatusCode.OK, """{ "data": { "productRetrieve": { "localizedGenres": [ { "value": "Action" } ] } } }""");

        var genres = await new PlayStationStoreClient(new HttpClient(handler)).FetchGenresAsync("EP9000-PPSA21567_00-0000000000000000");

        Assert.Equal(["Action & adventure"], genres);
        Assert.Contains("operationName=queryRetrieveTelemetryDataPDPProduct", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Contains("EP9000-PPSA21567_00-0000000000000000", Uri.UnescapeDataString(handler.Request.RequestUri.AbsoluteUri));
    }

    [Fact]
    public async Task Nothing_is_asked_for_an_empty_title()
    {
        var handler = new FakeStore(HttpStatusCode.OK, SearchJson);

        Assert.Empty(await new PlayStationStoreClient(new HttpClient(handler)).SearchGamesAsync("  "));
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "refus")]          // "Query not whitelisted": the site changed
    [InlineData(HttpStatusCode.TooManyRequests, "limite")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task An_error_of_the_store_is_explained(HttpStatusCode status, string expected)
    {
        var client = new PlayStationStoreClient(new HttpClient(new FakeStore(status, """{"message":"Query not whitelisted"}""")));

        var error = await Assert.ThrowsAsync<PlayStationException>(() => client.SearchGamesAsync("astro"));

        Assert.Contains(expected, error.Message);
    }

    private sealed class FakeStore(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        // Read while the request is alive: the client disposes it once sent.
        public string? ContentType { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
