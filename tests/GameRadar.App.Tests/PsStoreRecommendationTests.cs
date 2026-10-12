using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using GameRadar.App.Resources;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

/// <summary>
/// The PlayStation Store catalog as a source of recommendations: the box of the
/// filter, ticked by default for a player with a PlayStation history, and when
/// the catalog is downloaded. A game added by hand as the history, cached
/// Game Pass and Steam catalogs, and a fake network that sees who asks the store.
/// </summary>
public sealed class PsStoreRecommendationTests : AppTest
{
    private static ManualGame Hero(ManualPlatform platform) => new("Hero Quest", platform, ManualLevel.Completed, SteamAppId: "100");

    private static readonly CatalogGame HeroInGamePass =
        new("GP-HERO", "Hero Quest", null, null, ["Role playing"], OnPc: true, OnConsole: true, LeavingSoon: false);

    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    private async Task PrepareAsync(ManualPlatform platform, bool? showPlayStationStore = null, TimeSpan? psStoreAge = null)
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.Add(Hero(platform));
        settings.ShowPlayStationStore = showPlayStationStore;
        await store.SaveAsync(settings);

        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync("gamepass-catalog-fr-v6", TestJson.Serialize(new GameCatalog([HeroInGamePass], DateTimeOffset.UtcNow - Fresh)));
        await cache.SaveAsync("steam-catalog-fr-v3", TestJson.Serialize(new GameCatalog([], DateTimeOffset.UtcNow - Fresh)));
        if (psStoreAge is { } age)
            await cache.SaveAsync(PlayStationStoreCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([CachedPsRpg], DateTimeOffset.UtcNow - age)));
    }

    private static readonly CatalogGame CachedPsRpg =
        new("psstore:EP-CACHED", "Cached Ps Rpg", null, null, ["Role playing"], OnPc: false, OnConsole: false, LeavingSoon: false, PlayStationProductId: "EP-CACHED");

    private static IEnumerable<string> Cards(MainWindowViewModel vm) =>
        vm.Recommendations.Sections.SelectMany(s => s.Games).Select(g => g.Title);

    [AvaloniaFact]
    public async Task A_player_with_a_PlayStation_game_gets_the_box_ticked_and_the_store_games()
    {
        await PrepareAsync(ManualPlatform.PlayStation);
        var network = new PsStoreNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.True(vm.SourceFilter.ShowPlayStationStore);
        Assert.True(await BecomesTrueAsync(() => Cards(vm).Contains("Fresh Ps Rpg"), 5000));
        Assert.NotEmpty(network.StoreRequests);
        Assert.Contains("PlayStation Store", vm.Recommendations.Status);
        // The rule, not a choice: nothing saved, the box keeps following the history.
        Assert.Null((await SavedSettingsAsync(vm)).ShowPlayStationStore);
    }

    [AvaloniaFact]
    public async Task Without_a_PlayStation_game_the_box_is_unticked_and_the_store_never_asked()
    {
        await PrepareAsync(ManualPlatform.Switch);
        var network = new PsStoreNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();
        await vm.Catalogs.RefreshAllCommand.ExecuteAsync(null);

        Assert.False(vm.SourceFilter.ShowPlayStationStore);
        Assert.Empty(network.StoreRequests);
        Assert.DoesNotContain(vm.Catalogs.PsStore, vm.Catalogs.ShownLines);
    }

    [AvaloniaFact]
    public async Task Ticking_the_box_downloads_the_catalog_and_remembers_the_choice()
    {
        await PrepareAsync(ManualPlatform.Switch);
        var network = new PsStoreNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);
        await vm.InitializeAsync();

        vm.SourceFilter.ShowPlayStationStore = true;

        Assert.True(await BecomesTrueAsync(() => Cards(vm).Contains("Fresh Ps Rpg"), 5000));
        Assert.Contains(vm.Catalogs.PsStore, vm.Catalogs.ShownLines);
        Assert.True((await SavedSettingsAsync(vm)).ShowPlayStationStore);
    }

    [AvaloniaFact]
    public async Task Ticking_the_box_with_a_fresh_catalog_shows_its_games_without_downloading()
    {
        // Unticked, the store's games are not candidates (no Steam tag search for them);
        // ticked, the recommendations are computed again with them.
        await PrepareAsync(ManualPlatform.Switch, psStoreAge: Fresh);
        var network = new PsStoreNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);
        await vm.InitializeAsync();
        Assert.DoesNotContain("Cached Ps Rpg", Cards(vm));

        vm.SourceFilter.ShowPlayStationStore = true;

        Assert.True(await BecomesTrueAsync(() => Cards(vm).Contains("Cached Ps Rpg"), 5000));
        Assert.Empty(network.StoreRequests);
    }

    [AvaloniaFact]
    public async Task The_players_choice_beats_the_rule()
    {
        // A PlayStation player who unticked the box: it stays unticked, the store is not asked.
        await PrepareAsync(ManualPlatform.PlayStation, showPlayStationStore: false);
        var network = new PsStoreNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.False(vm.SourceFilter.ShowPlayStationStore);
        Assert.Empty(network.StoreRequests);
        Assert.False((await SavedSettingsAsync(vm)).ShowPlayStationStore);
    }

    [AvaloniaFact]
    public async Task A_fresh_cached_catalog_is_not_downloaded_again_at_startup()
    {
        // The box ticked by the rule (a PlayStation game) while the window is built, before the caches are read.
        await PrepareAsync(ManualPlatform.PlayStation, psStoreAge: Fresh);
        var network = new PsStoreNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.True(vm.SourceFilter.ShowPlayStationStore);
        Assert.Empty(network.StoreRequests);
        Assert.Contains("Cached Ps Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task An_old_catalog_recommends_at_once_while_the_new_one_downloads()
    {
        // The store download takes ~26 s: the cached catalog recommends meanwhile, like the Game Pass's.
        await PrepareAsync(ManualPlatform.PlayStation, psStoreAge: TimeSpan.FromDays(10));
        var network = new PsStoreNetwork { Hold = true };
        var vm = SampleData.Empty(TempFolder, network: network);

        var startup = vm.InitializeAsync();

        Assert.True(await BecomesTrueAsync(() => Cards(vm).Contains("Cached Ps Rpg"), 5000));
        Assert.Equal(Strings.Status_FetchingPsStoreCatalog, vm.Catalogs.PsStore.Status);
        network.Release();
        await startup;
        Assert.Contains("Fresh Ps Rpg", Cards(vm));
    }

    /// <summary>Answers the PlayStation Store with one best seller and its page, the other stores like <see cref="FakeStores"/>, and notes who asked the store.</summary>
    private sealed class PsStoreNetwork : HttpMessageHandler
    {
        private readonly FakeStores _others = new();
        private readonly List<Uri> _storeRequests = [];
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The store answers only once <see cref="Release"/> is called.</summary>
        public bool Hold { get; init; }

        public void Release() => _released.TrySetResult();

        public IReadOnlyList<Uri> StoreRequests
        {
            get
            {
                lock (_storeRequests)
                    return [.. _storeRequests];
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "web.np.playstation.com")
                return await new HttpMessageInvoker(_others).SendAsync(request, ct);

            lock (_storeRequests)
                _storeRequests.Add(uri);
            if (Hold)
                await _released.Task.WaitAsync(ct);
            string query = Uri.UnescapeDataString(uri.Query);
            string body = !query.Contains("categoryGridRetrieve")
                ? """{ "data": { "productRetrieve": { "localizedGenres": [ { "value": "Role Playing Games" } ], "starRating": { "averageRating": 4.5 } } } }"""
                // The PS5 best sellers: one game; no pre-order, nothing on PS4.
                : query.Contains("future_release_date") || !query.Contains("4cbf39e2")
                    ? """{ "data": { "categoryGridRetrieve": { "products": [] } } }"""
                    : """{ "data": { "categoryGridRetrieve": { "products": [ { "id": "EP-FRESH", "name": "Fresh Ps Rpg", "storeDisplayClassification": "FULL_GAME", "media": [] } ] } } }""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
