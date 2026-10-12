using System.Net;
using GameRadar.Core.GamePass;
using GameRadar.Core.Manual;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Manual;

/// <summary>"Ajouter un jeu" also searches the Microsoft Store: the Xbox games outside the Game Pass (exclusives...).</summary>
public class XboxStoreSearchTests
{
    // Shape of the real answer (productFamilies/autosuggest): add-ons are "Durable".
    private const string Json = """
        { "Results": [ { "Products": [
            { "ProductId": "9N1", "Type": "Game", "Title": "Halo 3" },
            { "ProductId": "9N2", "Type": "Durable", "Title": "Halo 3 Skin Pack" },
            { "ProductId": "9N3", "Type": "Game", "Title": "Halo: Reach" }
        ] } ] }
        """;

    [Fact]
    public void Suggestions_keep_the_games_only()
    {
        var games = GamePassClient.ParseSuggestions(Json);

        Assert.Equal(["Halo 3", "Halo: Reach"], games.Select(g => g.Title));
        Assert.Equal("9N1", games[0].ProductId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "Results": [] }""")]
    public void An_empty_answer_has_no_suggestion(string json)
    {
        Assert.Empty(GamePassClient.ParseSuggestions(json));
    }

    [Fact]
    public async Task The_search_asks_the_store_for_games_and_reads_the_answer()
    {
        var handler = new FakeStore(HttpStatusCode.OK, Json);

        var games = await new GamePassClient(new HttpClient(handler)).SuggestGamesAsync("halo 3");

        Assert.Equal(2, games.Count);
        Assert.Contains("productFamilyNames=Games", handler.Url);
        Assert.Contains("query=halo%203", handler.Url);
    }

    [Fact]
    public async Task A_store_that_does_not_know_the_title_gives_nothing()
    {
        var games = await new GamePassClient(new HttpClient(new FakeStore(HttpStatusCode.NotFound, ""))).SuggestGamesAsync("zzz");

        Assert.Empty(games);
    }

    [Fact]
    public async Task A_store_error_is_explained()
    {
        await Assert.ThrowsAsync<GamePassException>(
            () => new GamePassClient(new HttpClient(new FakeStore(HttpStatusCode.InternalServerError, ""))).SuggestGamesAsync("halo"));
    }

    [Fact]
    public async Task Nothing_is_asked_for_an_empty_title()
    {
        var handler = new FakeStore(HttpStatusCode.OK, Json);

        Assert.Empty(await new GamePassClient(new HttpClient(handler)).SuggestGamesAsync("  "));
        Assert.Null(handler.Url);
    }

    // ---------- Merge ----------

    private static StoreGameSuggestion Store(string title) => new("9N" + title.Length, title);

    [Fact]
    public void An_xbox_game_found_nowhere_else_is_a_line_of_its_own()
    {
        var merged = ManualGameSearch.Merge([], [], [Store("Halo 3")]);

        var line = Assert.Single(merged);
        Assert.Equal("Halo 3", line.Title);
        Assert.True(line.OnXbox);
        Assert.False(line.InGamePass);
        Assert.Null(line.SteamAppId);
    }

    [Fact]
    public void An_xbox_game_also_in_the_game_pass_is_one_line()
    {
        var fromCatalog = ManualGameSearch.InCatalog([Catalog("1", title: "Halo Infinite")], "halo");

        var line = Assert.Single(ManualGameSearch.Merge(fromCatalog, [], [Store("Halo Infinite")]));

        Assert.True(line.InGamePass);
        Assert.True(line.OnXbox);
    }

    [Fact]
    public void An_xbox_game_also_on_steam_is_one_line_with_its_app_id()
    {
        // The store sells an edition: the same game by its title key.
        var line = Assert.Single(ManualGameSearch.Merge([], [new SteamSuggestion("1145360", "Hades")], [Store("Hades Standard Edition")]));

        Assert.Equal("1145360", line.SteamAppId);
        Assert.True(line.OnSteam);
        Assert.True(line.OnXbox);
    }

    [Fact]
    public void Store_titles_are_cleaned_and_editions_make_one_line()
    {
        var merged = ManualGameSearch.Merge([], [], [Store("Control Ultimate Edition"), Store("Control")]);

        Assert.Equal("Control", Assert.Single(merged).Title);
    }

    [Fact]
    public void Without_the_store_the_merge_is_as_before()
    {
        var merged = ManualGameSearch.Merge([], [new SteamSuggestion("379720", "DOOM")]);

        Assert.False(Assert.Single(merged).OnXbox);
    }

    private sealed class FakeStore(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
