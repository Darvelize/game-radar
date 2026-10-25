using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

/// <summary>
/// "Ajouter un jeu": search (Game Pass catalog + Steam store, faked here: no
/// network in the tests), pick, say where and how far, add; several in a row.
/// </summary>
public sealed class AddGameTests : AppTest
{
    // The Steam store search, answering for "hades" (a game and its DLC).
    private static readonly FakeSteam Steam = new("""
        { "response": { "store_items": [
            { "success": 1, "appid": 1145360, "name": "Hades", "type": 0 },
            { "success": 1, "appid": 1145350, "name": "Hades II", "type": 0 },
            { "success": 1, "appid": 999, "name": "Hades Soundtrack", "type": 3 }
        ] } }
        """);

    [AvaloniaFact]
    public async Task A_new_search_runs_while_a_slow_one_waits()
    {
        // Steam slow on "halo": the player types "doom" and presses Enter, which checks CanExecute.
        var network = new HoldingNetwork("api.steampowered.com");
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.AddGame.Text = "halo";
        var slow = vm.AddGame.SearchCommand.ExecuteAsync(null);
        await network.Reached.Task;

        vm.AddGame.Text = "doom";

        Assert.True(vm.AddGame.SearchCommand.CanExecute(null));
        network.Release();
        await slow;
    }

    [AvaloniaFact]
    public async Task The_search_offers_the_games_found_and_never_a_dlc()
    {
        var vm = SampleData.Empty(TempFolder, network: Steam);
        vm.AddGame.Text = "hades";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["Hades", "Hades II"], vm.AddGame.Results.Select(r => r.Title));
        // "hades" is already a result: no "as typed" line.
        Assert.DoesNotContain(vm.AddGame.Results, r => r.IsAsTyped);
        Assert.Equal("Hades", vm.AddGame.SelectedResult?.Title);
        Assert.Equal("", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task A_game_found_nowhere_can_still_be_added_as_typed()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeSteam("""{ "response": { "store_items": [] } }"""));
        vm.AddGame.Text = "Golden Sun";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.AddGame.Results);
        Assert.True(row.IsAsTyped);
        Assert.Equal("Golden Sun", row.Title);
        Assert.Contains("Golden Sun", row.Label);
        Assert.Equal("Aucun jeu trouvé : tu peux l'ajouter tel quel.", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task Steam_unreachable_still_offers_the_title_as_typed()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeSteam(null));
        vm.AddGame.Text = "Hades";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.AddGame.Results).IsAsTyped);
        Assert.Contains("Steam", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task An_xbox_game_outside_the_game_pass_and_steam_is_offered()
    {
        // An exclusive: the Microsoft Store knows it, nothing else does.
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""",
            store: """{ "Results": [ { "Products": [ { "ProductId": "9N1", "Type": "Game", "Title": "Halo 3" } ] } ] }"""));
        vm.AddGame.Text = "halo 3";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.AddGame.Results);
        Assert.Equal("Halo 3", row.Title);
        Assert.True(row.OnXbox);
        Assert.False(row.IsAsTyped);
        Assert.Null(row.SteamAppId);
        Assert.Equal("", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task The_store_unreachable_keeps_the_steam_results_and_says_so()
    {
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [ { "success": 1, "appid": 1145360, "name": "Hades", "type": 0 } ] } }""",
            store: null));
        vm.AddGame.Text = "hades";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Hades", Assert.Single(vm.AddGame.Results).Title);
        Assert.Contains("Xbox", vm.AddGame.Status);
        Assert.DoesNotContain("Steam", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task Both_stores_unreachable_name_both()
    {
        var vm = SampleData.Empty(TempFolder, network: new Routed(steam: null, store: null));
        vm.AddGame.Text = "hades";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.AddGame.Results).IsAsTyped);
        Assert.Contains("Steam", vm.AddGame.Status);
        Assert.Contains("Xbox", vm.AddGame.Status);
    }

    private const string AstroBot = """
        { "data": { "universalSearch": { "results": [
          { "__typename": "Product", "id": "EP9000-PPSA21567_00-0000", "name": "ASTRO BOT™", "storeDisplayClassification": "FULL_GAME", "platforms": ["PS5"] },
          { "__typename": "Product", "id": "EP9000-PPSA21567_00-DDE", "name": "ASTRO BOT Deluxe Edition", "storeDisplayClassification": "PREMIUM_EDITION", "platforms": ["PS5"] }
        ] } } }
        """;

    [AvaloniaFact]
    public async Task A_playstation_exclusive_is_offered_with_its_product_id()
    {
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }""", playStation: AstroBot));
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.AddGame.Results);
        Assert.Equal("ASTRO BOT", row.Title);
        Assert.True(row.OnPlayStation);
        Assert.True(row.ShowPlayStationBadge);
        Assert.Equal("EP9000-PPSA21567_00-0000", row.PlayStationProductId);
        Assert.False(row.IsAsTyped);
        Assert.Equal("", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task A_store_that_refuses_falls_back_on_the_ps_plus_catalog_and_says_so()
    {
        // The site changed: its persisted queries are refused. The PS Plus catalog (the player's plan) still answers.
        await new SettingsStore(TempFolder).SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }""", playStation: null));
        vm.Catalogs.PsPlus.Catalog = new GameCatalog(
            [new CatalogGame("psplus:1", "Astro Bot", null, null, ["Platformer"], false, false, false, PsPlusTiers: PsPlusTier.Extra | PsPlusTier.Premium)],
            DateTimeOffset.UtcNow);
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.AddGame.Results);
        Assert.Equal("Astro Bot", row.Title);
        Assert.True(row.OnPlayStation);
        Assert.Equal(["Platformer"], row.Genres);
        Assert.Contains("PlayStation", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task The_ps_plus_catalog_is_not_searched_without_a_plan()
    {
        // No plan chosen: the catalog is not even downloaded, and its games are not the player's.
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }"""));
        vm.Catalogs.PsPlus.Catalog = new GameCatalog(
            [new CatalogGame("psplus:1", "Astro Bot", null, null, ["Platformer"], false, false, false, PsPlusTiers: PsPlusTier.Extra)],
            DateTimeOffset.UtcNow);
        vm.AddGame.Text = "astro";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.AddGame.Results).IsAsTyped);
    }

    // ---------- Genres of a game found only in the PlayStation Store ----------

    private const string AstroBotGenres = """{ "data": { "productRetrieve": { "localizedGenres": [ { "value": "Action" }, { "value": "Role Playing Games" } ] } } }""";

    [AvaloniaFact]
    public async Task A_playstation_exclusive_is_added_with_its_genres()
    {
        var network = new PlayStationStore(AstroBot, AstroBotGenres);
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.AddGame.Text = "astro bot";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);
        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.PlayStation);

        await vm.AddGame.AddCommand.ExecuteAsync(null);

        var saved = Assert.Single((await SavedSettingsAsync(vm)).ManualGames);
        Assert.Equal("ASTRO BOT", saved.Title);
        Assert.Equal(["Action & adventure", "Role playing"], saved.Genres);
        // One request, for the game chosen only.
        Assert.Equal(1, network.GenreRequests);
        Assert.Contains("ASTRO BOT", vm.AddGame.Confirmation);
    }

    [AvaloniaFact]
    public async Task The_genres_failing_does_not_refuse_the_game()
    {
        var network = new PlayStationStore(AstroBot, genres: null);
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.AddGame.Text = "astro bot";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        await vm.AddGame.AddCommand.ExecuteAsync(null);

        // Added without genres, as any game found nowhere.
        var saved = Assert.Single((await SavedSettingsAsync(vm)).ManualGames);
        Assert.Null(saved.Genres);
        Assert.Contains("ASTRO BOT", vm.AddGame.Confirmation);
    }

    [AvaloniaFact]
    public async Task A_ps_plus_game_is_added_with_the_genres_of_the_catalog_and_no_request()
    {
        await new SettingsStore(TempFolder).SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });
        var network = new PlayStationStore(search: """{ "data": { "universalSearch": { "results": [] } } }""", genres: AstroBotGenres);
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.Catalogs.PsPlus.Catalog = new GameCatalog(
            [new CatalogGame("psplus:1", "Astro Bot", null, null, ["Platformer"], false, false, false, PsPlusTiers: PsPlusTier.Extra)],
            DateTimeOffset.UtcNow);
        vm.AddGame.Text = "astro bot";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        await vm.AddGame.AddCommand.ExecuteAsync(null);

        var saved = Assert.Single((await SavedSettingsAsync(vm)).ManualGames);
        Assert.Equal(["Platformer"], saved.Genres);
        Assert.Equal(0, network.GenreRequests);
    }

    [AvaloniaFact]
    public async Task A_game_found_on_steam_asks_no_genres_of_playstation()
    {
        var network = new PlayStationStore(NoPlayStationGame, AstroBotGenres, steam: Steam);
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.AddGame.Text = "hades";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        await vm.AddGame.AddCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single((await SavedSettingsAsync(vm)).ManualGames).Genres);
        Assert.Equal(0, network.GenreRequests);
    }

    // ---------- The platform follows the store the game was found in ----------

    [AvaloniaFact]
    public async Task A_playstation_exclusive_presets_the_playstation_platform()
    {
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }""", playStation: AstroBot));
        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.Switch);
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal(ManualPlatform.PlayStation, vm.AddGame.Platform.Platform);
    }

    [AvaloniaFact]
    public async Task An_xbox_exclusive_presets_the_xbox_platform()
    {
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""",
            store: """{ "Results": [ { "Products": [ { "ProductId": "9N1", "Type": "Game", "Title": "Halo 3" } ] } ] }"""));
        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.Switch);
        vm.AddGame.Text = "halo 3";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal(ManualPlatform.OldXbox, vm.AddGame.Platform.Platform);
    }

    [AvaloniaFact]
    public async Task A_game_found_on_steam_leaves_the_platform_as_chosen()
    {
        // A Steam game may have been played on any platform: the player's choice stays.
        var vm = SampleData.Empty(TempFolder, network: Steam);
        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.Switch);
        vm.AddGame.Text = "hades";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal(ManualPlatform.Switch, vm.AddGame.Platform.Platform);
    }

    [AvaloniaFact]
    public async Task The_player_can_still_change_the_preset_platform()
    {
        var vm = SampleData.Empty(TempFolder, network: new Routed(
            steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }""", playStation: AstroBot));
        vm.AddGame.Text = "astro bot";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.Other);

        Assert.Equal(ManualPlatform.Other, vm.AddGame.Platform.Platform);
    }

    // ---------- A PlayStation Store in trouble: the highest PS Plus plan, whatever the player has ----------

    private static CatalogGame PlusGame(string id, string title, PsPlusTier tiers) =>
        new($"psplus:{id}", title, null, null, ["Platformer"], false, false, false, PsPlusTiers: tiers);

    private static readonly CatalogGame[] PlusCatalog =
    [
        PlusGame("1", "Astro Bot", PsPlusTier.Extra | PsPlusTier.Premium),
        PlusGame("2", "Old Classic", PsPlusTier.Premium)
    ];

    private Routed RefusingStore(string? psPlusLists = null) => new(
        steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }""", playStation: null, psPlusLists: psPlusLists);

    private Task SeedPsPlusCacheAsync()
    {
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        return cache.SaveAsync(new PsPlusCatalogSource(cache).CacheKey,
            TestJson.Serialize(new GameCatalog(PlusCatalog, DateTimeOffset.UtcNow.AddDays(-30))));
    }

    [AvaloniaFact]
    public async Task A_store_that_refuses_searches_the_premium_catalog_even_for_an_extra_subscriber()
    {
        // The player has Extra, and "Old Classic" is Premium only: with the store down, the top plan is searched.
        await new SettingsStore(TempFolder).SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });
        var vm = SampleData.Empty(TempFolder, network: RefusingStore());
        vm.Catalogs.PsPlus.Catalog = new GameCatalog(PlusCatalog, DateTimeOffset.UtcNow);
        vm.AddGame.Text = "old classic";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.AddGame.Results);
        Assert.Equal("Old Classic", row.Title);
        Assert.True(row.OnPlayStation);
        Assert.Contains("PlayStation", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task A_store_that_refuses_searches_the_catalog_even_without_any_plan()
    {
        // No plan, so the catalog was never downloaded for the player: the one on disk (any age) is used.
        await SeedPsPlusCacheAsync();
        var vm = SampleData.Empty(TempFolder, network: RefusingStore());
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.AddGame.Results);
        Assert.Equal("Astro Bot", row.Title);
        Assert.Equal(["Platformer"], row.Genres);
    }

    [AvaloniaFact]
    public async Task A_store_that_refuses_downloads_the_catalog_once_when_there_is_none()
    {
        // No plan and nothing on disk: the player's own search asks for it (the one request to playstation.com).
        var vm = SampleData.Empty(TempFolder, network: RefusingStore(
            psPlusLists: """[ { "games": [ { "conceptId": 1, "name": "Astro Bot", "nameEn": "Astro Bot" } ] } ]"""));
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Astro Bot", Assert.Single(vm.AddGame.Results).Title);
        // Kept on disk: the next search does not download it again.
        Assert.NotNull(new PsPlusCatalogSource(new CacheStore(Path.Combine(TempFolder, "cache"))).LoadCached());
    }

    [AvaloniaFact]
    public async Task A_store_that_refuses_with_no_catalog_to_be_had_still_offers_the_title_as_typed()
    {
        var vm = SampleData.Empty(TempFolder, network: RefusingStore());
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.AddGame.Results).IsAsTyped);
        Assert.Contains("PlayStation", vm.AddGame.Status);
    }

    [AvaloniaFact]
    public async Task A_working_store_downloads_nothing_for_a_player_without_a_plan()
    {
        // The rule stays: no request to playstation.com's lists unless the store is down.
        var network = new Routed(steam: """{ "response": { "store_items": [] } }""", store: """{ "Results": [] }""");
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.AddGame.Text = "astro bot";

        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        Assert.Equal(0, network.PsPlusListRequests);
    }

    [AvaloniaFact]
    public async Task Adding_saves_the_game_shows_it_and_clears_the_page()
    {
        var vm = SampleData.Empty(TempFolder, network: Steam);
        vm.AddGame.Text = "hades";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);
        vm.AddGame.Platform = ManualGameOptions.Of(ManualPlatform.Switch);
        vm.AddGame.Level = ManualGameOptions.Of(ManualLevel.Completed);

        vm.AddGame.AddCommand.Execute(null);

        Assert.Contains("Hades", vm.AddGame.Confirmation);
        Assert.Empty(vm.AddGame.Results);
        Assert.Equal("", vm.AddGame.Text);
        // Kept for the next game: the same console, the same level likely.
        Assert.Equal(ManualPlatform.Switch, vm.AddGame.Platform.Platform);
        var row = Assert.Single(vm.History.Games);
        Assert.Equal("Nintendo", row.ManualPlatformLabel);
        var saved = await SavedSettingsAsync(vm);
        Assert.Equal(new ManualGame("Hades", ManualPlatform.Switch, ManualLevel.Completed, "1145360"), Assert.Single(saved.ManualGames));
    }

    [AvaloniaFact]
    public async Task Adding_the_same_game_again_updates_it()
    {
        var vm = SampleData.Empty(TempFolder, network: Steam);
        for (int i = 0; i < 2; i++)
        {
            vm.AddGame.Text = "hades";
            await vm.AddGame.SearchCommand.ExecuteAsync(null);
            vm.AddGame.Level = ManualGameOptions.Of(i == 0 ? ManualLevel.PlayedALittle : ManualLevel.Mastered);
            vm.AddGame.AddCommand.Execute(null);
        }

        Assert.Contains("mis à jour", vm.AddGame.Confirmation);
        Assert.Equal("Platiné", Assert.Single(vm.History.Games).StatusLabel);
    }

    [AvaloniaFact]
    public async Task A_search_ending_after_an_addition_leaves_the_page_cleared()
    {
        // The first search answers at once, the second one waits.
        var steam = new HeldAfterFirstSearch();
        var vm = SampleData.Empty(TempFolder, network: steam);
        vm.AddGame.Text = "hades";
        await vm.AddGame.SearchCommand.ExecuteAsync(null);

        // Retyped and searched again, then "Ajouter" before Steam answers.
        vm.AddGame.Text = "hades ii";
        var searching = vm.AddGame.SearchCommand.ExecuteAsync(null);
        vm.AddGame.AddCommand.Execute(null);
        steam.Release();
        await searching;

        // No result picked behind the player's back for the next click.
        Assert.Empty(vm.AddGame.Results);
        Assert.Null(vm.AddGame.SelectedResult);
        Assert.Equal("", vm.AddGame.Status);
        Assert.Contains("Hades", vm.AddGame.Confirmation);
    }

    [AvaloniaFact]
    public void The_page_opens_from_the_history_and_goes_back_to_it()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.NavigateCommand.Execute(AppPage.History);

        vm.AddGame.OpenCommand.Execute(null);
        Assert.Equal(AppPage.AddGame, vm.CurrentPage);
        vm.GoBackCommand.Execute(null);

        Assert.Equal(AppPage.History, vm.CurrentPage);
    }

    /// <summary>The Steam store search answering "hades": at once the first time, after <see cref="Release"/> then.</summary>
    private sealed class HeldAfterFirstSearch : HttpMessageHandler
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public void Release() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host.Contains("steampowered") && Interlocked.Increment(ref _requests) > 1)
                await _released.Task.WaitAsync(ct);
            return await new HttpMessageInvoker(Steam).SendAsync(request, ct);
        }
    }

    /// <summary>A valid PlayStation Store answer with no game: what the tests that are not about it get.</summary>
    private const string NoPlayStationGame = """{ "data": { "universalSearch": { "results": [] } } }""";

    private static HttpResponseMessage Answer(string? json, HttpStatusCode failure = HttpStatusCode.ServiceUnavailable) =>
        json is null
            ? new HttpResponseMessage(failure)
            : FakeStores.Json(json);

    /// <summary>
    /// The Steam search, the Microsoft Store search and the PlayStation Store search each answer
    /// their JSON, or fail for null (503; the PlayStation Store refuses with 400, as when its site changes).
    /// </summary>
    private sealed class Routed(string? steam, string? store, string? playStation = NoPlayStationGame, string? psPlusLists = null) : HttpMessageHandler
    {
        private int _psPlusListRequests;

        public int PsPlusListRequests => _psPlusListRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string host = request.RequestUri!.Host;
            if (host == "www.playstation.com")
            {
                // The PS Plus lists (the fallback of a store in trouble): none unless the test gives some.
                Interlocked.Increment(ref _psPlusListRequests);
                return Task.FromResult(Answer(psPlusLists ?? "[]"));
            }
            return Task.FromResult(host.Contains("playstation") ? Answer(playStation, HttpStatusCode.BadRequest)
                : host.Contains("displaycatalog") ? Answer(store)
                : Answer(steam));
        }
    }

    /// <summary>The Steam store search: the given JSON, or 503 for null. The PlayStation Store: no game.</summary>
    private sealed class FakeSteam(string? json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(request.RequestUri!.Host.Contains("playstation") ? Answer(NoPlayStationGame) : Answer(json));
    }

    /// <summary>The three stores: Steam (given, else empty), the Microsoft Store (empty) and the PlayStation Store (search, then genres by operation).</summary>
    private sealed class PlayStationStore(string search, string? genres, HttpMessageHandler? steam = null) : HttpMessageHandler
    {
        private int _genreRequests;

        public int GenreRequests => _genreRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host.Contains("playstation"))
            {
                if (!uri.AbsoluteUri.Contains("queryRetrieveTelemetryDataPDPProduct"))
                    return Answer(search);
                Interlocked.Increment(ref _genreRequests);
                return Answer(genres);
            }
            if (uri.Host.Contains("displaycatalog"))
                return Answer("""{ "Results": [] }""");
            return steam is null
                ? Answer("""{ "response": { "store_items": [] } }""")
                : await new HttpMessageInvoker(steam).SendAsync(request, ct);
        }
    }
}
