using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Steam;

public class SteamGenreSourceTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeStoresHandler _handler = new();
    private readonly HttpClient _http;

    public SteamGenreSourceTests() => _http = new HttpClient(_handler);

    public void Dispose()
    {
        _http.Dispose();
        _folder.Dispose();
    }

    private SteamGenreSource Source() =>
        new(new CacheStore(_folder.Path), "FR", TimeSpan.Zero, TimeSpan.Zero);

    private Task<IReadOnlyList<PlayedGameWithGenres>> Resolve(SteamGenreSource source, CatalogMatcher matcher, params PlayedGame[] games) =>
        source.ResolveAsync(games, matcher, new GamePassClient(_http), new SteamStoreClient(_http));

    private static PlayedGame SteamGame(string appId, string name, int minutes = 600) =>
        // No achievements: only the play time tells about taste.
        Played(appId, name: name, platform: "Steam", hasAchievements: false) with { PlaytimeMinutes = minutes };

    [Fact]
    public void PickSearchResult_KeepsOnlyTheGameWithTheExactTitle()
    {
        const string json = """
            { "Results": [
                { "Products": [ { "ProductId": "ADDON", "Type": "Durable", "Title": "Hades" } ] },
                { "Products": [ { "ProductId": "SEQUEL", "Type": "Game", "Title": "Hades II" } ] },
                { "Products": [ { "ProductId": "GAME", "Type": "Game", "Title": "HADES™" } ] }
            ] }
            """;

        Assert.Equal("GAME", GamePassClient.PickSearchResult(json, "Hades"));
        Assert.Null(GamePassClient.PickSearchResult(json, "Hades III"));
    }

    [Fact]
    public void PickSearchResult_FindsTheGameUnderAnEditionTitleButPrefersTheExactOne()
    {
        const string edition = """
            { "Results": [
                { "Products": [ { "ProductId": "EDITION", "Type": "Game", "Title": "Control Ultimate Edition - Xbox Series X|S" } ] },
                { "Products": [ { "ProductId": "SEQUEL", "Type": "Game", "Title": "Control 2" } ] }
            ] }
            """;
        const string both = """
            { "Results": [
                { "Products": [ { "ProductId": "EDITION", "Type": "Game", "Title": "Diablo® IV - Standard Edition" } ] },
                { "Products": [ { "ProductId": "GAME", "Type": "Game", "Title": "Diablo® IV" } ] }
            ] }
            """;

        // One title rule everywhere (GameTitle.Key), as the catalog matcher.
        Assert.Equal("EDITION", GamePassClient.PickSearchResult(edition, "Control"));
        Assert.Equal("GAME", GamePassClient.PickSearchResult(both, "Diablo IV"));
    }

    [Fact]
    public void SteamGenres_AreTranslatedAndBusinessModelsDropped()
    {
        var features = SteamStoreClient.ParseFeatures("""
            { "7": { "success": true, "data": {
                "genres": [ { "description": "Action" }, { "description": "Indie" }, { "description": "Adventure" }, { "description": "RPG" }, { "description": "Free To Play" } ],
                "categories": [ { "description": "Single-player" }, { "description": "Online Co-op" }, { "description": "Multi-player" }, { "description": "Steam Cloud" } ]
            } } }
            """, "7");

        Assert.NotNull(features);
        Assert.Equal(["Action & adventure", "Role playing"], features.Genres);
        Assert.Equal(PlayModes.Solo | PlayModes.CoopOnline, features.Modes);
    }

    [Fact]
    public void SteamGenres_OnlyBusinessModelsGiveNothing()
    {
        Assert.Null(SteamStoreClient.ParseFeatures("""
            { "7": { "success": true, "data": { "genres": [ { "description": "Indie" }, { "description": "Casual" } ] } } }
            """, "7"));
    }

    [Fact]
    public void SteamGenres_AnEmptyArrayForDataGivesNothing()
    {
        // What the store answers for a game with neither genres nor categories.
        Assert.Null(SteamStoreClient.ParseFeatures("""{ "7": { "success": true, "data": [] } }""", "7"));
    }

    [Fact]
    public async Task Resolve_GoesOnWithoutTheLookupsWhenTheMicrosoftStoreFails()
    {
        var matcher = new CatalogMatcher([Catalog("GP1", title: "In Game Pass", genres: ["Shooter"])]);
        _handler.ProductsFail = true;

        // The games already known still count; the others wait for the next time.
        var result = await Resolve(Source(), matcher, SteamGame("1", "In Game Pass"), SteamGame("2", "On The Store"));

        Assert.Equal(["1"], result.Select(r => r.Game.PlatformId));
        _handler.ProductsFail = false;
        _handler.Calls.Clear();
        result = await Resolve(Source(), matcher, SteamGame("1", "In Game Pass"), SteamGame("2", "On The Store"));
        Assert.Contains("search:On The Store", _handler.Calls);
        Assert.Equal(["1", "2"], result.Select(r => r.Game.PlatformId));
    }

    [Fact]
    public async Task Resolve_DoesNotTakeASteamOutageForAGameFoundNowhere()
    {
        var matcher = new CatalogMatcher([]);
        _handler.AppDetailsDown = true;

        // The Steam store answers 503: no genres this time, but not "not found"
        // for 30 days either.
        Assert.Empty(await Resolve(Source(), matcher, SteamGame("3", "Steam Only")));

        _handler.AppDetailsDown = false;
        _handler.Calls.Clear();
        var result = await Resolve(Source(), matcher, SteamGame("3", "Steam Only"));
        Assert.Contains("appdetails:3", _handler.Calls);
        Assert.Equal(["Role playing"], Assert.Single(result).Genres);
    }

    [Fact]
    public async Task Resolve_UsesCatalogThenMicrosoftStoreThenSteamPage()
    {
        var matcher = new CatalogMatcher([Catalog("GP1", title: "In Game Pass", genres: ["Shooter"])]);

        var result = await Resolve(Source(), matcher,
            SteamGame("1", "In Game Pass"),
            SteamGame("2", "On The Store"),
            SteamGame("3", "Steam Only"),
            SteamGame("4", "Nowhere"));

        Assert.Equal(["Shooter"], result.Single(r => r.Game.PlatformId == "1").Genres);
        Assert.Equal(["Shooter", "Role playing"], result.Single(r => r.Game.PlatformId == "2").Genres);
        Assert.Equal(["Role playing"], result.Single(r => r.Game.PlatformId == "3").Genres);
        Assert.DoesNotContain(result, r => r.Game.PlatformId == "4");
        // The catalog game costs no request; the Steam page is only asked for the others.
        Assert.DoesNotContain("search:In Game Pass", _handler.Calls);
        Assert.Equal(["appdetails:3", "appdetails:4"], _handler.Calls.Where(c => c.StartsWith("appdetails")));
    }

    [Fact]
    public async Task Resolve_FindsTheGogGamesByNameTheSameWay()
    {
        var gogGame = Played("1207658930", name: "On The Store", platform: "GOG", hasAchievements: false) with { PlaytimeMinutes = 600 };

        var result = await Resolve(Source(), new CatalogMatcher([]), gogGame);

        Assert.Equal("On The Store", Assert.Single(result).Game.Name);
    }

    [Fact]
    public async Task Resolve_FindsTheEaGamesByNameTheSameWay()
    {
        var eaGame = Played("anthem", name: "On The Store", platform: "EA", hasAchievements: false) with { PlaytimeMinutes = 600 };

        var result = await Resolve(Source(), new CatalogMatcher([]), eaGame);

        Assert.Equal("On The Store", Assert.Single(result).Game.Name);
    }

    [Fact]
    public async Task Resolve_FindsTheGamesAddedByHandTheSameWay()
    {
        var matcher = new CatalogMatcher([]);

        var result = await Resolve(Source(), matcher,
            ManualGames.ToPlayed(new ManualGame("On The Store", ManualPlatform.PlayStation, ManualLevel.PlayedALot)),
            ManualGames.ToPlayed(new ManualGame("Steam Only", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "3")),
            ManualGames.ToPlayed(new ManualGame("Nowhere", ManualPlatform.Other, ManualLevel.Completed)));

        Assert.Equal(["On The Store", "Steam Only"], result.Select(r => r.Game.Name));
        Assert.Equal(["Role playing"], result.Single(r => r.Game.Name == "Steam Only").Genres);
        // Found nowhere on Steam: no page to ask for.
        Assert.Equal(["appdetails:3"], _handler.Calls.Where(c => c.StartsWith("appdetails")));
    }

    [Fact]
    public async Task Resolve_RemembersResultsEvenNotFound()
    {
        var matcher = new CatalogMatcher([]);
        await Resolve(Source(), matcher, SteamGame("2", "On The Store"), SteamGame("4", "Nowhere"));
        _handler.Calls.Clear();

        var result = await Resolve(Source(), matcher, SteamGame("2", "On The Store"), SteamGame("4", "Nowhere"));

        Assert.Empty(_handler.Calls);
        Assert.Single(result);
    }

    [Fact]
    public async Task Resolve_SkipsGamesThatSayNothingAboutTaste()
    {
        var result = await Resolve(Source(), new CatalogMatcher([]), SteamGame("2", "On The Store", minutes: 0));

        Assert.Empty(result);
        Assert.Empty(_handler.Calls);
    }

    /// <summary>Answers like the Microsoft Store and the Steam store, and records the calls.</summary>
    private sealed class FakeStoresHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public bool ProductsFail { get; set; }
        public bool AppDetailsDown { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            HttpResponseMessage response;

            if (ProductsFail && uri.AbsolutePath.EndsWith("/products"))
                throw new HttpRequestException("The Microsoft Store cannot be reached.");
            if (AppDetailsDown && uri.AbsolutePath.EndsWith("/appdetails"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (uri.AbsolutePath.EndsWith("/autosuggest"))
            {
                Calls.Add("search:" + query["query"]);
                response = query["query"] == "On The Store"
                    ? Json("""{ "Results": [ { "Products": [ { "ProductId": "MS2", "Type": "Game", "Title": "On The Store" } ] } ] }""")
                    : Json("""{ "Results": [] }""");
            }
            else if (uri.AbsolutePath.EndsWith("/products"))
            {
                Calls.Add("products:" + query["bigIds"]);
                response = Json("""
                    { "Products": [ { "ProductId": "MS2", "LocalizedProperties": [ { "ProductTitle": "On The Store" } ],
                      "Properties": { "Category": "Shooter", "Categories": [ "Role playing" ] } } ] }
                    """);
            }
            else if (uri.AbsolutePath.EndsWith("/appdetails"))
            {
                string appId = query["appids"]!;
                Calls.Add("appdetails:" + appId);
                response = appId == "3"
                    ? Json("""{ "3": { "success": true, "data": { "genres": [ { "description": "RPG" } ] } } }""")
                    : Json($$"""{ "{{appId}}": { "success": false } }""");
            }
            else
                response = new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
