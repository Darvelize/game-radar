using System.Collections.Concurrent;
using System.Net;
using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

/// <summary>
/// The "Jeux gratuits" page: its five sources, when they are downloaded (only once
/// the page is opened, at most once a day), and a source that fails. A fake network
/// that answers Epic, Steam, playstation.com, Amazon and xbox.com, and notes who was asked.
/// </summary>
public sealed class FreeGamesTests : AppTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private const string SteamGivenAway = "Steam games given away";

    private static string Iso(DateTimeOffset date) => date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>Answers the five sources with one game each, and notes the hosts asked.</summary>
    private sealed class FreeGamesNetwork(HttpStatusCode amazonStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public ConcurrentBag<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string host = request.RequestUri!.Host;
            Hosts.Add(host);
            // The Steam catalog uses the same host: the free games have their own query.
            if (Uri.UnescapeDataString(request.RequestUri.Query).Contains("min_discount_percent"))
                Hosts.Add(SteamGivenAway);
            if (host == "luna.amazon.com" && request.Method == HttpMethod.Post && amazonStatus != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(amazonStatus));
            string? body = host switch
            {
                "store-site-backend-static.ak.epicgames.com" => $$"""
                    { "data": { "Catalog": { "searchStore": { "elements": [
                      { "title": "Epic Gift", "offerType": "BASE_GAME", "productSlug": "epic-gift",
                        "keyImages": [], "catalogNs": { "mappings": [] },
                        "price": { "totalPrice": { "discountPrice": 0 } },
                        "promotions": { "promotionalOffers": [ { "promotionalOffers": [
                          { "startDate": "{{Iso(Now.AddDays(-1))}}", "endDate": "{{Iso(Now.AddDays(6))}}", "discountSetting": { "discountPercentage": 0 } } ] } ] } } ] } } } }
                    """,
                "api.steampowered.com" => $$"""
                    { "response": { "store_items": [
                      { "success": 1, "appid": 2807960, "name": "Steam Weekend", "free_weekend": { "start_time": {{Now.AddDays(-1).ToUnixTimeSeconds()}}, "end_time": {{Now.AddDays(2).ToUnixTimeSeconds()}} } } ] } }
                    """,
                "www.playstation.com" => """
                    [ { "games": [ { "conceptId": 234014, "nameEn": "Monthly Hunt" } ] } ]
                    """,
                "emerald.xboxservices.com" => """
                    { "channels": { "BROWSE_CHANNELID=FREE-PLAY-DAYS_FILTERS=": { "products": [ { "productId": "9P4NX9RD7FRG" } ] } },
                      "productSummaries": [ { "productId": "9P4NX9RD7FRG", "title": "Free Play Raven", "productKind": "Game" } ] }
                    """,
                "luna.amazon.com" when request.Method == HttpMethod.Get =>
                    "<input type='hidden' name='csrf-key' value='token' />",
                "luna.amazon.com" => $$"""
                    { "data": { "items": { "items": [
                      { "category": "FULL_GAME",
                        "assets": { "title": "Prime Gift", "externalClaimLink": "https://gaming.amazon.com/prime-gift-gog/dp/1" },
                        "offers": [ { "startTime": "{{Iso(Now.AddDays(-1))}}", "endTime": "{{Iso(Now.AddDays(20))}}" } ] } ] } } }
                    """,
                _ => null
            };
            return Task.FromResult(body is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : FakeStores.Json(body));
        }
    }

    private CacheStore Cache => new(Path.Combine(TempFolder, "cache"));

    private static FreeGameSection Section(MainWindowViewModel vm, string title) =>
        vm.FreeGames.Sections.Single(s => s.Title == title);

    private static IEnumerable<string> Titles(FreeGameSection section) => section.Games.Select(g => g.Title);

    [AvaloniaFact]
    public async Task Nothing_is_asked_before_the_page_is_opened()
    {
        var network = new FreeGamesNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.DoesNotContain("store-site-backend-static.ak.epicgames.com", network.Hosts);
        Assert.DoesNotContain("luna.amazon.com", network.Hosts);
        Assert.DoesNotContain(SteamGivenAway, network.Hosts);
    }

    [AvaloniaFact]
    public async Task Going_to_the_page_asks_its_lists()
    {
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        vm.NavigateCommand.Execute(AppPage.FreeGames);

        Assert.True(vm.IsFreeGamesPage);
        Assert.True(await BecomesTrueAsync(() => Section(vm, "Epic Games").Games.Count == 1, 5000));
    }

    [AvaloniaFact]
    public async Task Opening_the_page_shows_the_free_games_of_Epic_and_Amazon()
    {
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        Assert.Equal(["Epic Games", "Steam", "PlayStation Plus", "Amazon Prime", "Xbox"], vm.FreeGames.Sections.Select(s => s.Title));
        Assert.Equal(["Epic Gift"], Titles(Section(vm, "Epic Games")));
        var prime = Assert.Single(Section(vm, "Amazon Prime").Games);
        Assert.Equal("Prime Gift", prime.Title);
        Assert.Contains("GOG", prime.RedeemLabel);
        Assert.Equal(new Uri("https://gaming.amazon.com/prime-gift-gog/dp/1"), prime.Page);
    }

    [AvaloniaFact]
    public async Task A_Steam_free_weekend_says_it_is_not_to_keep_and_opens_Steam_in_the_apps_language()
    {
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        var weekend = Assert.Single(Section(vm, "Steam").Games);
        Assert.Equal("Steam Weekend", weekend.Title);
        Assert.Contains("sans le garder", weekend.OfferLabel);
        Assert.Equal(new Uri("https://store.steampowered.com/app/2807960/?l=french"), weekend.Page);
    }

    [AvaloniaFact]
    public async Task A_Steam_game_given_away_is_free_until_the_end_of_the_offer()
    {
        await new FreeGameCache(Cache, FreeGameCache.SteamKey).SaveAsync([new FreeGame("Steam Gift", "https://store.steampowered.com/app/10/?snr=1_7#reviews", Now.AddDays(2))]);
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        var gift = Assert.Single(Section(vm, "Steam").Games);
        Assert.StartsWith("Gratuit jusqu'au", gift.OfferLabel);
        // A query already in the link gives way to the language.
        Assert.Equal(new Uri("https://store.steampowered.com/app/10/?l=french#reviews"), gift.Page);
    }

    [AvaloniaFact]
    public async Task Without_a_PS_Plus_plan_playstation_com_is_never_asked()
    {
        var network = new FreeGamesNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.FreeGames.OpenAsync();

        Assert.DoesNotContain("www.playstation.com", network.Hosts);
        var psPlus = Section(vm, "PlayStation Plus");
        Assert.True(psPlus.ShowsHint);
        Assert.Empty(psPlus.Games);
    }

    [AvaloniaFact]
    public async Task A_Game_Pass_player_has_the_Free_Play_Days_asked()
    {
        // The default plan: Ultimate. (Their end is computed from today: not shown from Monday to Wednesday.)
        var network = new FreeGamesNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.FreeGames.OpenAsync();

        Assert.Contains("emerald.xboxservices.com", network.Hosts);
        Assert.False(Section(vm, "Xbox").ShowsHint);
        Assert.Equal("Free Play Raven", Assert.Single(new FreeGameCache(Cache, FreeGameCache.XboxKey).Load()!.Games).Title);
    }

    [AvaloniaFact]
    public async Task A_Free_Play_Days_game_is_free_with_Game_Pass_and_opens_the_Store_in_the_apps_language()
    {
        await new FreeGameCache(Cache, FreeGameCache.XboxKey).SaveAsync(
            [new FreeGame("Free Play Raven", "https://www.xbox.com/games/store/-/9P4NX9RD7FRG", Now.AddDays(1), PlayOnly: true)]);
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        var xbox = Section(vm, "Xbox");
        Assert.Empty(xbox.Kept.Games);
        var game = Assert.Single(xbox.Timed.Games);
        Assert.StartsWith("Jouable gratuitement avec le Game Pass jusqu'au", game.OfferLabel);
        Assert.Equal(new Uri("https://www.xbox.com/fr-FR/games/store/-/9P4NX9RD7FRG"), game.Page);
    }

    [AvaloniaFact]
    public async Task Without_a_Game_Pass_plan_xbox_com_is_never_asked()
    {
        await new SettingsStore(TempFolder).SaveAsync(new AppSettings { GamePassPlan = GamePassTier.None });
        var network = new FreeGamesNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.FreeGames.OpenAsync();

        Assert.DoesNotContain("emerald.xboxservices.com", network.Hosts);
        var xbox = Section(vm, "Xbox");
        Assert.True(xbox.ShowsHint);
        Assert.False(xbox.Timed.ShowsNone);
        Assert.Contains("Game Pass", xbox.Hint);
        Assert.Empty(xbox.Games);
    }

    [AvaloniaFact]
    public async Task An_Essential_player_sees_the_games_of_the_month()
    {
        await new SettingsStore(TempFolder).SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Essential });
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        var psPlus = Section(vm, "PlayStation Plus");
        Assert.False(psPlus.ShowsHint);
        var game = Assert.Single(psPlus.Games);
        Assert.Equal("Monthly Hunt", game.Title);
        Assert.Contains("/concept/234014", game.Page.ToString());
    }

    [AvaloniaFact]
    public async Task A_fresh_list_is_not_asked_again()
    {
        await new FreeGameCache(Cache, FreeGameCache.EpicKey).SaveAsync([new FreeGame("Cached Gift", "https://store.epicgames.com/p/cached", Now.AddDays(2))]);
        var network = new FreeGamesNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.FreeGames.OpenAsync();

        Assert.DoesNotContain("store-site-backend-static.ak.epicgames.com", network.Hosts);
        Assert.Equal(["Cached Gift"], Titles(Section(vm, "Epic Games")));
    }

    [AvaloniaFact]
    public async Task Leaving_the_plan_takes_its_former_error_away()
    {
        // Xbox down, then the player says "Aucun" Game Pass: the hint alone, not the old error above it.
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        await vm.FreeGames.OpenAsync();
        Assert.NotEqual("", Section(vm, "Xbox").Status);

        vm.SourceFilter.SelectedGamePassPlan = vm.SourceFilter.GamePassPlans.Single(p => p.Plan == GamePassTier.None);
        await vm.FreeGames.OpenAsync();

        Assert.True(Section(vm, "Xbox").ShowsHint);
        Assert.Equal("", Section(vm, "Xbox").Status);
    }

    [AvaloniaFact]
    public async Task A_picture_that_failed_is_tried_again_at_the_next_visit()
    {
        // The card is kept for the whole offer: a picture missed once (no network yet) must come back.
        await new FreeGameCache(Cache, FreeGameCache.EpicKey).SaveAsync(
            [new FreeGame("Cached Gift", "https://store.epicgames.com/p/cached", Now.AddDays(2), ImageUrl: "https://images.example/gift.png")]);
        var pictures = new FlakyPictures();
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork(), imageNetwork: pictures);
        await vm.FreeGames.OpenAsync();
        var card = Assert.Single(Section(vm, "Epic Games").Games);
        Assert.Null(card.Image);

        pictures.Answers = true;
        await vm.FreeGames.OpenAsync();

        Assert.NotNull(card.Image);
    }

    /// <summary>Refuses every picture until <see cref="Answers"/>, then sends a 1 x 1 PNG.</summary>
    private sealed class FlakyPictures : HttpMessageHandler
    {
        public bool Answers { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!Answers)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var png = new ByteArrayContent(SampleData.TinyPng);
            png.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = png });
        }
    }

    [AvaloniaFact]
    public async Task A_list_whose_offer_ended_is_asked_again()
    {
        // Epic changed its games of the week: the ended one is never shown, the new one is asked
        // (the list was read more than an hour ago).
        await Cache.SaveAsync(FreeGameCache.EpicKey, TestJson.Serialize(
            new FreeGameList([new FreeGame("Last Week", "https://store.epicgames.com/p/last", Now.AddMinutes(-5))], Now.AddHours(-2))));
        var network = new FreeGamesNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.FreeGames.OpenAsync();

        Assert.Equal(["Epic Gift"], Titles(Section(vm, "Epic Games")));
    }

    [AvaloniaFact]
    public async Task A_source_that_fails_keeps_its_list_and_says_why_while_the_others_show()
    {
        // Older than a day: asked again.
        await Cache.SaveAsync(FreeGameCache.AmazonKey, TestJson.Serialize(
            new FreeGameList([new FreeGame("Old Prime Gift", "https://gaming.amazon.com/old-gog/dp/1", Now.AddHours(1))], Now.AddDays(-2))));
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork(amazonStatus: HttpStatusCode.Forbidden));

        await vm.FreeGames.OpenAsync();

        var amazon = Section(vm, "Amazon Prime");
        Assert.Equal(["Old Prime Gift"], Titles(amazon));
        Assert.NotNull(amazon.Error);
        Assert.Equal(["Epic Gift"], Titles(Section(vm, "Epic Games")));
        // Said on the page: no banner over every page.
        Assert.False(vm.Notices.HasMessage);
    }

    [AvaloniaFact]
    public async Task A_free_game_already_in_the_history_says_so()
    {
        // Added by hand under another case and a ™: the same game (GameTitle.Key).
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.Add(new ManualGame("EPIC GIFT™", ManualPlatform.OtherPcStore, ManualLevel.PlayedALittle));
        await store.SaveAsync(settings);
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        Assert.True(Section(vm, "Epic Games").Games.Single().InHistory);
        Assert.False(Section(vm, "Amazon Prime").Games.Single().InHistory);
    }

    [AvaloniaFact]
    public async Task A_section_without_any_game_says_so()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());

        await vm.FreeGames.OpenAsync();

        Assert.True(Section(vm, "Epic Games").Kept.ShowsNone);
        Assert.False(Section(vm, "PlayStation Plus").Kept.ShowsNone);
    }

    [AvaloniaFact]
    public async Task The_games_to_keep_and_those_free_for_a_while_are_in_two_tabs()
    {
        await new FreeGameCache(Cache, FreeGameCache.SteamKey).SaveAsync([
            new FreeGame("Steam Gift", "https://store.steampowered.com/app/10/", Now.AddDays(2)),
            new FreeGame("Steam Weekend", "https://store.steampowered.com/app/20/", Now.AddDays(1), PlayOnly: true)]);
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        Assert.Equal(["Epic Games", "Steam", "PlayStation Plus", "Amazon Prime"], vm.FreeGames.KeptSections.Select(s => s.Section.Title));
        Assert.Equal(["Steam", "Xbox"], vm.FreeGames.TimedSections.Select(s => s.Section.Title));
        var steam = Section(vm, "Steam");
        // A free weekend is never among the games to keep, and the reverse.
        Assert.Equal(["Steam Gift"], steam.Kept.Games.Select(g => g.Title));
        Assert.Equal(["Steam Weekend"], steam.Timed.Games.Select(g => g.Title));
        Assert.False(steam.Kept.ShowsNone);
        Assert.False(steam.Timed.ShowsNone);
    }

    [AvaloniaFact]
    public async Task Coming_back_to_the_page_keeps_its_cards()
    {
        // Same cards, same pictures: nothing decoded again on each visit.
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());
        await vm.FreeGames.OpenAsync();
        var first = Section(vm, "Epic Games").Games.Single();

        await vm.FreeGames.OpenAsync();

        Assert.Same(first, Section(vm, "Epic Games").Games.Single());
    }

    [AvaloniaFact]
    public async Task A_link_outside_the_stores_site_is_never_shown()
    {
        await new FreeGameCache(Cache, FreeGameCache.EpicKey).SaveAsync(
        [
            new FreeGame("Elsewhere", "https://example.com/p/elsewhere", Now.AddDays(2)),
            new FreeGame("Not https", "http://store.epicgames.com/p/plain", Now.AddDays(2)),
            new FreeGame("Kept", "https://store.epicgames.com/p/kept", Now.AddDays(2)),
        ]);
        var vm = SampleData.Empty(TempFolder, network: new FreeGamesNetwork());

        await vm.FreeGames.OpenAsync();

        var kept = Assert.Single(Section(vm, "Epic Games").Games);
        Assert.Equal("Kept", kept.Title);
        Assert.Matches("^https://store.epicgames.com/(fr|en-US)/p/kept$", kept.Page.ToString());
    }
}
