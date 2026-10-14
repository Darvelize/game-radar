using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using static GameRadar.App.Tests.CachedCatalogs;

namespace GameRadar.App.Tests;

/// <summary>
/// The PS Plus catalog as a source of recommendations: the plan, the box of the
/// filter, and when the catalog is downloaded. Cached catalogs, a game added by
/// hand as the history, and a fake network that sees who is asked.
/// </summary>
public sealed class PsPlusRecommendationTests : AppTest
{
    private static readonly ManualGame Hero = new("Hero Quest", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "100");

    // As the real lists give them: an Extra game is also in Premium, a classic only in Premium.
    private static readonly CatalogGame ExtraRpg = PsPlusGame("1", "Cozy Rpg", PsPlusTier.Extra | PsPlusTier.Premium);
    private static readonly CatalogGame ClassicRpg = PsPlusGame("2", "Old Rpg", PsPlusTier.Premium);

    private static CatalogGame PsPlusGame(string id, string title, PsPlusTier tiers) =>
        new($"psplus:{id}", title, null, null, ["Role playing"], OnPc: false, OnConsole: false, LeavingSoon: false, PsPlusTiers: tiers);

    /// <summary>The history, the Game Pass and Steam caches, and the PS Plus one when an age is given.</summary>
    private async Task PrepareAsync(PsPlusTier plan, TimeSpan? psPlusAge, Action<AppSettings>? withSettings = null)
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.Add(Hero);
        settings.PsPlusPlan = plan;
        withSettings?.Invoke(settings);
        await store.SaveAsync(settings);

        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await SaveGamePassAndSteamAsync(cache);
        if (psPlusAge is { } age)
            await cache.SaveAsync(new PsPlusCatalogSource(cache).CacheKey, TestJson.Serialize(new GameCatalog([ExtraRpg, ClassicRpg], DateTimeOffset.UtcNow - age)));
    }

    [AvaloniaFact]
    public void Without_a_saved_plan_there_is_no_PS_Plus_box()
    {
        var vm = SampleData.Empty(TempFolder);

        Assert.Equal(PsPlusTier.None, vm.SourceFilter.PsPlusPlan);
        Assert.False(vm.SourceFilter.HasPsPlusPlan);
        Assert.True(vm.SourceFilter.ShowPsPlus);
    }

    [AvaloniaFact]
    public async Task The_plan_and_the_box_are_remembered_after_a_restart()
    {
        await PrepareAsync(PsPlusTier.Premium, psPlusAge: null, settings => settings.ShowPsPlus = false);

        var vm = SampleData.Empty(TempFolder);

        Assert.Equal(PsPlusTier.Premium, vm.SourceFilter.PsPlusPlan);
        Assert.Equal("PlayStation Plus Premium", vm.SourceFilter.PsPlusLabel);
        Assert.True(vm.SourceFilter.HasPsPlusPlan);
        Assert.False(vm.SourceFilter.ShowPsPlus);
    }

    [AvaloniaFact]
    public async Task Starting_the_app_does_not_save_one_box_over_the_others()
    {
        // Each hook writes only its own setting: loading one box must not save
        // another before it is loaded (the "Steam" box once came back unticked).
        await PrepareAsync(PsPlusTier.Extra, psPlusAge: null, settings =>
        {
            settings.ShowSteam = false;
            settings.ShowGamePass = false;
            settings.ShowPsPlus = false;
        });

        var vm = SampleData.Empty(TempFolder);
        vm.SourceFilter.ShowPsPlus = true;
        await vm.WhenSettingsSavedAsync();

        var saved = new SettingsStore(TempFolder).Load();
        Assert.True(saved.ShowPsPlus);
        Assert.False(saved.ShowSteam);
        Assert.False(saved.ShowGamePass);
        Assert.Equal(PsPlusTier.Extra, saved.PsPlusPlan);
    }

    [AvaloniaFact]
    public async Task An_Extra_player_gets_the_Extra_games_only()
    {
        await PrepareAsync(PsPlusTier.Extra, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.Contains("Cozy Rpg", Cards(vm));
        Assert.DoesNotContain("Old Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task A_Premium_player_gets_the_classics_too()
    {
        await PrepareAsync(PsPlusTier.Premium, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.Contains("Cozy Rpg", Cards(vm));
        Assert.Contains("Old Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task Without_a_plan_no_PS_Plus_game_is_recommended()
    {
        await PrepareAsync(PsPlusTier.None, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.DoesNotContain("Cozy Rpg", Cards(vm));
        Assert.DoesNotContain("Old Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task Unticking_the_PS_Plus_box_takes_its_games_away()
    {
        await PrepareAsync(PsPlusTier.Extra, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        await vm.InitializeAsync();
        Assert.Contains("Cozy Rpg", Cards(vm));

        vm.SourceFilter.ShowPsPlus = false;

        Assert.DoesNotContain("Cozy Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task The_summary_names_the_PS_Plus_plan_among_the_sources()
    {
        await PrepareAsync(PsPlusTier.Extra, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.Contains("PlayStation Plus Extra", vm.Recommendations.Status);
    }

    [AvaloniaFact]
    public async Task The_catalog_is_not_downloaded_for_a_player_without_a_plan()
    {
        // No subscription: not one request to playstation.com, not even an error banner.
        await PrepareAsync(PsPlusTier.None, psPlusAge: null);
        var network = new PsPlusNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.Empty(network.PsPlusRequests);
        Assert.Null(vm.Library.PsPlusCatalog);
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(10)]
    public async Task A_missing_or_old_catalog_is_downloaded_at_startup_for_a_player_with_a_plan(int? ageInDays)
    {
        await PrepareAsync(PsPlusTier.Extra, ageInDays is { } days ? TimeSpan.FromDays(days) : null);
        var network = new PsPlusNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.NotEmpty(network.PsPlusRequests);
        Assert.Equal(["Fresh Rpg", "Fresh Classic"], vm.Library.PsPlusCatalog!.Games.Select(g => g.Title));
        Assert.Contains("Fresh Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task A_fresh_catalog_is_not_downloaded_again()
    {
        await PrepareAsync(PsPlusTier.Extra, Fresh);
        var network = new PsPlusNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.Empty(network.PsPlusRequests);
    }

    [AvaloniaFact]
    public async Task Choosing_a_plan_downloads_the_catalog_and_shows_its_games()
    {
        await PrepareAsync(PsPlusTier.None, psPlusAge: null);
        var network = new PsPlusNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);
        await vm.InitializeAsync();
        Assert.Empty(network.PsPlusRequests);

        vm.SourceFilter.SelectedPsPlusPlan = vm.SourceFilter.PsPlusPlans.Single(o => o.Plan == PsPlusTier.Extra);

        Assert.True(await BecomesTrueAsync(() => Cards(vm).Contains("Fresh Rpg"), 5000));
        await vm.WhenSettingsSavedAsync();
        Assert.Equal(PsPlusTier.Extra, new SettingsStore(TempFolder).Load().PsPlusPlan);
    }

    [AvaloniaFact]
    public async Task A_plan_chosen_before_the_caches_are_read_keeps_a_fresh_catalog()
    {
        await PrepareAsync(PsPlusTier.None, Fresh);
        var network = new PsPlusNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        // While the window is built, before the startup reads the caches: no catalog yet in memory.
        vm.SourceFilter.SelectedPsPlusPlan = vm.SourceFilter.PsPlusPlans.Single(o => o.Plan == PsPlusTier.Extra);
        await vm.InitializeAsync();

        Assert.Empty(network.PsPlusRequests);
        Assert.Contains("Cozy Rpg", Cards(vm));
    }

    [AvaloniaFact]
    public async Task A_refresh_of_everything_skips_PS_Plus_without_a_plan()
    {
        await PrepareAsync(PsPlusTier.None, psPlusAge: null);
        var network = new PsPlusNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.Catalogs.RefreshAllCommand.ExecuteAsync(null);

        Assert.Empty(network.PsPlusRequests);
    }

    /// <summary>Answers the PS Plus lists with one game, the other stores like <see cref="FakeStores"/>, and notes who asked playstation.com.</summary>
    private sealed class PsPlusNetwork : HttpMessageHandler
    {
        private readonly FakeStores _others = new();
        private readonly List<Uri> _psPlusRequests = [];

        public IReadOnlyList<Uri> PsPlusRequests => _psPlusRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "www.playstation.com")
                return await new HttpMessageInvoker(_others).SendAsync(request, ct);

            lock (_psPlusRequests)
                _psPlusRequests.Add(request.RequestUri);
            // One Extra game and one classic, as the real lists (a catalog without classics would be refused as incomplete).
            string body = request.RequestUri.Query.Contains("plus-games-list")
                ? """[{ "games": [ { "conceptId": 9, "name": "Fresh Rpg", "nameEn": "Fresh Rpg", "genre": ["ROLE_PLAYING_GAMES"] } ] }]"""
                : request.RequestUri.Query.Contains("plus-classics-list")
                    ? """[{ "games": [ { "conceptId": 10, "name": "Fresh Classic", "nameEn": "Fresh Classic", "genre": ["ACTION"] } ] }]"""
                    : "[]";
            return FakeStores.Json(body);
        }
    }
}
