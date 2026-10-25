using System.Net;
using System.Text;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;

namespace GameRadar.Core.Tests.PlayStation;

public class PsPlusCatalogClientTests
{
    // Hand-written like the real lists: one block per letter, games inside.
    private const string GamesJson = """
        [
          { "catalogKey": "A", "count": 2, "games": [
            { "conceptId": 228903, "name": "A Hat in Time", "nameEn": "A Hat in Time",
              "conceptUrl": "https://store.playstation.com/fr-fr/concept/228903",
              "imageUrl": "https://image.api.playstation.com/cdn/hat.png",
              "genre": ["ADVENTURE", "ACTION", "ACTION", "UNIQUE"], "device": ["PS4"] },
            { "conceptId": 10008945, "name": "Un jeu", "nameEn": " Cozy Game ",
              "imageUrl": "https://image.api.playstation.com/cdn/cozy.png",
              "genre": ["MUSIC/RHYTHM", "PUZZLE"] }
          ] },
          { "catalogKey": "#", "count": 1, "games": [
            { "conceptId": 7, "name": "7th Sector", "nameEn": "7th Sector", "genre": [] }
          ] }
        ]
        """;

    [Fact]
    public void ParseJson_FlattensTheBlocksAndMapsTheFields()
    {
        var games = PsPlusCatalogClient.ParseJson(GamesJson, PsPlusTier.Extra | PsPlusTier.Premium);

        Assert.Equal(3, games.Count);
        var hat = games[0];
        Assert.Equal("psplus:228903", hat.ProductId);
        // The game's page on the PlayStation store is built from it.
        Assert.Equal("228903", hat.PlayStationConceptId);
        Assert.Equal("A Hat in Time", hat.Title);
        Assert.Equal("https://image.api.playstation.com/cdn/hat.png", hat.ImageUrl);
        Assert.Equal(PsPlusTier.Extra | PsPlusTier.Premium, hat.PsPlusTiers);
        // Not a Game Pass game: no platform, no leaving soon.
        Assert.False(hat.InGamePass);
        Assert.True(hat.InPsPlus);
    }

    [Fact]
    public void ParseJson_GivesTheEnglishTitleTrimmed()
    {
        var games = PsPlusCatalogClient.ParseJson(GamesJson, PsPlusTier.Extra);

        Assert.Equal("Cozy Game", games[1].Title);
    }

    [Fact]
    public void ParseJson_AnEmptyEnglishTitle_GivesWayToTheName()
    {
        string json = """[{ "games": [{ "conceptId": 1, "name": "Un jeu", "nameEn": "" }] }]""";

        Assert.Equal("Un jeu", Assert.Single(PsPlusCatalogClient.ParseJson(json, PsPlusTier.Extra)).Title);
    }

    [Fact]
    public void ParseJson_MapsGenresToTheStoreVocabularyWithoutRepeats()
    {
        var games = PsPlusCatalogClient.ParseJson(GamesJson, PsPlusTier.Extra);

        // ACTION twice and UNIQUE (says nothing of the kind of game): one genre each.
        Assert.Equal(["Action & adventure"], games[0].Genres);
        Assert.Equal(["Music", "Puzzle & trivia"], games[1].Genres);
        Assert.Empty(games[2].Genres);
    }

    [Fact]
    public void ParseJson_SkipsEntriesWithoutIdOrTitle()
    {
        const string json = """
            [ { "games": [
                { "name": "No id" },
                { "conceptId": 5 },
                { "conceptId": 6, "name": "Fine" },
                { }
            ] }, { "catalogKey": "B" } ]
            """;

        var games = PsPlusCatalogClient.ParseJson(json, PsPlusTier.Extra);

        Assert.Equal("Fine", Assert.Single(games).Title);
    }

    [Theory]
    [InlineData("<html>not found</html>")]
    [InlineData("{\"games\": []}")]
    [InlineData("")]
    public void ParseJson_RefusesAnUnexpectedAnswer(string json)
    {
        Assert.Throws<PlayStationException>(() => PsPlusCatalogClient.ParseJson(json, PsPlusTier.Extra));
    }

    private static string Block(params (int Id, string Name)[] games) =>
        "[{ \"games\": [" + string.Join(",", games.Select(g =>
            $"{{ \"conceptId\": {g.Id}, \"name\": \"{g.Name}\", \"nameEn\": \"{g.Name}\" }}")) + "] }]";

    [Fact]
    public async Task FetchCatalogAsync_GivesEachListItsPlans()
    {
        var handler = new FakeHandler(uri => uri.Query.Contains("plus-classics-list")
            ? Block((2, "Old Classic"))
            : uri.Query.Contains("ubisoft-classics-list")
                ? Block((3, "Ubi Classic"), (1, "Extra Game"))
                : Block((1, "Extra Game")));
        var client = new PsPlusCatalogClient(new HttpClient(handler));

        var games = await client.FetchCatalogAsync();

        Assert.Equal(3, games.Count);
        // Extra ⊂ Premium: an Extra game is in both, a classic only in Premium.
        Assert.Equal(PsPlusTier.Extra | PsPlusTier.Premium, games.Single(g => g.Title == "Extra Game").PsPlusTiers);
        Assert.Equal(PsPlusTier.Extra | PsPlusTier.Premium, games.Single(g => g.Title == "Ubi Classic").PsPlusTiers);
        Assert.Equal(PsPlusTier.Premium, games.Single(g => g.Title == "Old Classic").PsPlusTiers);
    }

    [Fact]
    public async Task FetchCatalogAsync_JoinsTheTwoVersionsOfAConcept()
    {
        // The real Extra list repeats a concept (one entry per console version).
        var handler = new FakeHandler(uri => uri.Query.Contains("plus-games-list")
            ? Block((1, "Elder Game"), (1, "Elder Game"))
            : "[]");
        var client = new PsPlusCatalogClient(new HttpClient(handler));

        var games = await client.FetchCatalogAsync();

        Assert.Single(games);
    }

    [Fact]
    public async Task FetchCatalogAsync_AsksTheFrenchListsWithoutAnySecret()
    {
        var handler = new FakeHandler(_ => "[]");
        var client = new PsPlusCatalogClient(new HttpClient(handler));

        await client.FetchCatalogAsync();

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Contains("/bin/imagic/gameslist", r.Uri.AbsolutePath);
            Assert.Contains("locale=fr-fr", r.Uri.Query);
            // The catalog is public: never the NPSSO or an access token.
            Assert.Null(r.Authorization);
        });
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FetchCatalogAsync_ExplainsAnHttpError(HttpStatusCode status)
    {
        var client = new PsPlusCatalogClient(new HttpClient(new FakeHandler(_ => "oops", status)));

        await Assert.ThrowsAsync<PlayStationException>(() => client.FetchCatalogAsync());
    }

    [Fact]
    public async Task FetchCatalogAsync_RefusesAPageThatIsNotTheList()
    {
        // A success status with an HTML page (the 404 page of an unknown locale is small and HTML).
        var client = new PsPlusCatalogClient(new HttpClient(new FakeHandler(_ => "<html>Page not found</html>")));

        await Assert.ThrowsAsync<PlayStationException>(() => client.FetchCatalogAsync());
    }

    private sealed record Seen(Uri Uri, string? Authorization);

    private sealed class FakeHandler(Func<Uri, string> answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new Seen(request.RequestUri!, request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "application/json")
            });
        }
    }
}

public class PsPlusMonthlyGamesTests
{
    // Like the real list: a game and its PS Plus starter pack share a concept.
    private const string MonthlyJson = """
        [
          { "catalogKey": "H", "count": 2, "games": [
            { "conceptId": 234014, "nameEn": "Hunt: Showdown 1896 – Starter Pack for PlayStation®Plus",
              "imageUrl": "https://image.api.playstation.com/cdn/pack.png", "releaseDate": "2026-10-06T08:00:00Z" },
            { "conceptId": 234014, "nameEn": "Hunt: Showdown 1896",
              "imageUrl": "https://image.api.playstation.com/cdn/hunt.png", "releaseDate": "2024-08-15T07:00:00Z" }
          ] },
          { "catalogKey": "F", "count": 1, "games": [
            { "conceptId": 10011830, "nameEn": "F1® 25", "releaseDate": "2025-05-30T15:00:00Z" }
          ] }
        ]
        """;

    [Fact]
    public void ParseMonthlyJson_GivesOneGamePerConcept_TheGameNotItsPack()
    {
        var games = PsPlusCatalogClient.ParseMonthlyJson(MonthlyJson);

        Assert.Equal(2, games.Count);
        var hunt = games.Single(g => g.Title == "Hunt: Showdown 1896");
        Assert.Equal("https://image.api.playstation.com/cdn/hunt.png", hunt.ImageUrl);
        // Without language: the page adds the app's.
        Assert.Equal("https://store.playstation.com/concept/234014", hunt.Link);
        // The list gives no end date.
        Assert.Null(hunt.Until);
    }

    [Fact]
    public void ParseMonthlyJson_CleansTheTitle()
    {
        Assert.Contains(PsPlusCatalogClient.ParseMonthlyJson(MonthlyJson), g => g.Title == "F1 25");
    }

    [Fact]
    public async Task FetchMonthlyGamesAsync_AsksTheMonthlyList()
    {
        Uri? asked = null;
        using var http = new HttpClient(new TestData.JsonHandler(uri =>
        {
            asked = uri;
            return MonthlyJson;
        }));

        var games = await new PsPlusCatalogClient(http).FetchMonthlyGamesAsync();

        Assert.Equal(2, games.Count);
        Assert.Contains("categoryList=plus-monthly-games-list", asked!.Query);
    }
}
