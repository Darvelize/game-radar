using System.Collections.Concurrent;
using System.Net;
using Avalonia.Headless.XUnit;
using GameRadar.App.Resources;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.GOG;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;

namespace GameRadar.App.Tests;

/// <summary>
/// The candidate catalogs, one line each: read from their cache, downloaded when
/// stale (only the stale ones, PS Plus only with a plan), and shown in Settings › Data.
/// </summary>
public sealed class CatalogLinesTests : AppTest
{
    private static readonly CatalogGame SteamRpg =
        new("steam:100", "Hero Quest", null, null, ["Role playing"], OnPc: true, OnConsole: false, LeavingSoon: false);

    [AvaloniaFact]
    public async Task Each_catalog_is_one_line_read_from_its_cache()
    {
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(SteamCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([SteamRpg], DateTimeOffset.UtcNow)));
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());

        await vm.Catalogs.LoadCachedAsync();
        vm.Catalogs.ShowLoaded();

        var catalogs = vm.Catalogs;
        Assert.Equal([catalogs.GamePass, catalogs.PsPlus, catalogs.Steam, catalogs.Gog, catalogs.Epic, catalogs.PsStore], catalogs.Lines);
        Assert.Equal("Hero Quest", Assert.Single(vm.Library.SteamCatalog!.Games).Title);
        Assert.StartsWith("1 ", catalogs.Steam.Status);
        Assert.Equal(Strings.Status_GogCatalogNotLoaded, catalogs.Gog.Status);
        // The Game Pass line is written once the history is built (it counts the games played).
        Assert.Equal("", catalogs.GamePass.Status);
    }

    [AvaloniaFact]
    public void Settings_Data_shows_the_PS_Plus_block_only_with_a_plan()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        var catalogs = vm.Catalogs;
        var shown = new List<string>();
        catalogs.PropertyChanged += (_, e) => shown.Add(e.PropertyName!);
        Assert.Equal([catalogs.GamePass, catalogs.Steam, catalogs.Gog, catalogs.Epic], catalogs.ShownLines);

        vm.SourceFilter.SelectedPsPlusPlan = vm.SourceFilter.PsPlusPlans.Single(p => p.Plan == PsPlusTier.Extra);

        Assert.Contains(nameof(catalogs.ShownLines), shown);
        Assert.Equal([catalogs.GamePass, catalogs.PsPlus, catalogs.Steam, catalogs.Gog, catalogs.Epic], catalogs.ShownLines);
    }

    [AvaloniaFact]
    public async Task Only_the_stale_catalogs_are_downloaded_at_startup()
    {
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await SaveAsync(cache, new GamePassCatalogSource(cache).CacheKey, age: TimeSpan.FromDays(1));
        await SaveAsync(cache, SteamCatalogSource.CacheKey, age: TimeSpan.FromDays(1));
        await SaveAsync(cache, GogCatalogSource.CacheKey, age: TimeSpan.FromDays(8));
        var network = new CatalogNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.Equal(0, network.Count("gamepass"));
        Assert.Equal(0, network.Count("steam"));
        Assert.True(network.Count("gog") > 0);
    }

    [AvaloniaFact]
    public async Task The_PS_Plus_catalog_is_never_downloaded_without_a_plan()
    {
        var network = new CatalogNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();
        await vm.Catalogs.RefreshAllCommand.ExecuteAsync(null);

        Assert.Equal(0, network.Count("psplus"));
        Assert.True(network.Count("gog") > 0);
    }

    [AvaloniaFact]
    public async Task The_PS_Plus_catalog_is_downloaded_with_a_plan()
    {
        await new SettingsStore(TempFolder).SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Extra });
        var network = new CatalogNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        await vm.InitializeAsync();

        Assert.True(network.Count("psplus") > 0);
    }

    [AvaloniaFact]
    public async Task A_failing_catalog_does_not_stop_the_others()
    {
        var network = new CatalogNetwork();
        var vm = SampleData.Empty(TempFolder, network: network);

        // Every catalog answers 404: each is asked anyway, each line says its own error.
        await vm.Catalogs.RefreshAllCommand.ExecuteAsync(null);

        Assert.True(network.Count("gamepass") > 0);
        Assert.True(network.Count("steam") > 0);
        Assert.True(network.Count("gog") > 0);
        Assert.Equal(GameRadar.Core.Resources.CoreStrings.Gog_CatalogDown, vm.Catalogs.Gog.Status);
        Assert.NotEqual(vm.Catalogs.Gog.Status, vm.Catalogs.Steam.Status);
        Assert.False(vm.Catalogs.IsRefreshing);
    }

    [AvaloniaFact]
    public async Task Only_a_catalog_that_shows_in_the_history_rebuilds_it_once_on_arrival()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        int rebuilt = 0;
        vm.History.Rebuilt += () => rebuilt++;
        // PS Plus games carry no history badge.
        Assert.False(vm.Catalogs.PsPlus.RebuildsHistory);

        await vm.Catalogs.RefreshAsync([Line(rebuildsHistory: false)]);
        Assert.Equal(0, rebuilt);

        // Each catalog counts as soon as it arrives: once each.
        await vm.Catalogs.RefreshAsync([Line(rebuildsHistory: true), Line(rebuildsHistory: true)]);
        Assert.Equal(2, rebuilt);
    }

    [AvaloniaFact]
    public async Task A_catalog_counts_as_soon_as_it_arrives_without_waiting_for_a_slow_one()
    {
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());
        int rebuilt = 0;
        vm.History.Rebuilt += () => rebuilt++;
        var slow = new TaskCompletionSource<GameCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new CatalogLine
        {
            Title = "Slow catalog",
            LogContext = "Slow catalog",
            FetchingStatus = "…",
            LoadCached = () => null,
            Refresh = () => slow.Task,
            ErrorMessage = ex => ex.Message
        };

        var refreshing = vm.Catalogs.RefreshAsync([held, Line(rebuildsHistory: true)]);

        // The quick one rebuilt the history and the recommendations followed (no history yet: they wait),
        // while the slow one still downloads.
        Assert.True(await BecomesTrueAsync(() => rebuilt == 1 && vm.Recommendations.Status == Strings.Reco_Waiting, 2000));
        Assert.True(vm.Catalogs.IsRefreshing);

        slow.SetResult(new GameCatalog([SteamRpg], DateTimeOffset.UtcNow));
        await refreshing;
        Assert.Equal(2, rebuilt);
        Assert.False(vm.Catalogs.IsRefreshing);
    }

    [AvaloniaFact]
    public async Task The_catalogs_are_downloaded_at_once_not_one_after_the_other()
    {
        var network = new CatalogNetwork { HoldGamePass = true };
        var vm = SampleData.Empty(TempFolder, network: network);

        var refreshing = vm.Catalogs.RefreshAllCommand.ExecuteAsync(null);
        await network.GamePassReached.Task;

        // The Game Pass is held: GOG ends meanwhile.
        Assert.True(await BecomesTrueAsync(() => vm.Catalogs.Gog.Status == GameRadar.Core.Resources.CoreStrings.Gog_CatalogDown, 2000));
        Assert.True(vm.Catalogs.IsRefreshing);

        network.Release();
        await refreshing;
        Assert.False(vm.Catalogs.IsRefreshing);
    }

    [AvaloniaFact]
    public async Task Overlapping_refreshes_keep_the_button_off_and_never_download_a_catalog_twice()
    {
        var network = new CatalogNetwork { HoldGamePass = true };
        var vm = SampleData.Empty(TempFolder, network: network);
        var startup = vm.Catalogs.RefreshAsync([vm.Catalogs.GamePass]);
        await network.GamePassReached.Task;

        // A short refresh ends during the Game Pass one (a PS Plus plan chosen meanwhile).
        await vm.Catalogs.RefreshAsync([Line(rebuildsHistory: false)]);
        Assert.True(vm.Catalogs.IsRefreshing);
        Assert.False(vm.Catalogs.RefreshAllCommand.CanExecute(null));
        // Asked again meanwhile: not downloaded a second time.
        await vm.Catalogs.RefreshAsync([vm.Catalogs.GamePass]);
        Assert.Equal(1, network.Count("gamepass"));

        network.Release();
        await startup;
        Assert.False(vm.Catalogs.IsRefreshing);
    }

    [AvaloniaFact]
    public async Task A_failed_Game_Pass_download_keeps_its_error_when_another_catalog_rebuilds_the_history()
    {
        var vm = SampleData.Empty(TempFolder, network: new CatalogNetwork());
        vm.Catalogs.GamePass.Catalog = new GameCatalog([SteamRpg], DateTimeOffset.UtcNow.AddDays(-8));
        await vm.Catalogs.RefreshAsync([vm.Catalogs.GamePass]);
        string error = vm.Catalogs.GamePass.Status;

        await vm.Catalogs.RefreshAsync([Line(rebuildsHistory: true)]);

        Assert.Equal(error, vm.Catalogs.GamePass.Status);
    }

    [AvaloniaFact]
    public async Task The_Game_Pass_line_says_it_downloads_while_another_catalog_rebuilds_the_history()
    {
        var network = new CatalogNetwork { HoldGamePass = true };
        var vm = SampleData.Empty(TempFolder, network: network);
        vm.Catalogs.GamePass.Catalog = new GameCatalog([SteamRpg], DateTimeOffset.UtcNow.AddDays(-8));
        int rebuilt = 0;
        vm.History.Rebuilt += () => rebuilt++;

        var refreshing = vm.Catalogs.RefreshAsync([vm.Catalogs.GamePass, Line(rebuildsHistory: true)]);
        await network.GamePassReached.Task;

        // The quick catalog rebuilt the history: the Game Pass line still downloads, and says so.
        Assert.True(await BecomesTrueAsync(() => rebuilt == 1, 2000));
        Assert.Equal(vm.Catalogs.GamePass.FetchingStatus, vm.Catalogs.GamePass.Status);

        network.Release();
        await refreshing;
    }

    [AvaloniaFact]
    public async Task The_PS_Plus_catalog_loaded_for_a_search_shows_on_its_line()
    {
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await SaveAsync(cache, new PsPlusCatalogSource(cache).CacheKey, age: TimeSpan.FromDays(1));
        var vm = SampleData.Empty(TempFolder, network: new CatalogNetwork());
        vm.Catalogs.ShowLoaded();
        Assert.Equal(Strings.Status_PsPlusCatalogNotLoaded, vm.Catalogs.PsPlus.Status);

        Assert.NotNull(await vm.Catalogs.GetPsPlusCatalogForSearchAsync());

        Assert.StartsWith("1 ", vm.Catalogs.PsPlus.Status);
    }

    private static Task SaveAsync(CacheStore cache, string key, TimeSpan age) =>
        cache.SaveAsync(key, TestJson.Serialize(new GameCatalog([SteamRpg], DateTimeOffset.UtcNow - age)));

    // A catalog that downloads at once, for what RefreshAsync does with its result.
    private static CatalogLine Line(bool rebuildsHistory) => new()
    {
        Title = "Test catalog",
        LogContext = "Test catalog",
        FetchingStatus = "…",
        LoadCached = () => null,
        Refresh = () => Task.FromResult(new GameCatalog([SteamRpg], DateTimeOffset.UtcNow)),
        ErrorMessage = ex => ex.Message,
        RebuildsHistory = rebuildsHistory
    };


    /// <summary>
    /// Every catalog answers 404, and its requests are counted ("gamepass",
    /// "steam", "psplus", "gog"); the Game Pass can be held until <see cref="Release"/>.
    /// </summary>
    private sealed class CatalogNetwork : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, int> _requests = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HoldGamePass { get; init; }
        public TaskCompletionSource GamePassReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count(string catalog) => _requests.GetValueOrDefault(catalog);

        public void Release() => _released.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            string? catalog = uri.Host is "catalog.gamepass.com" or "displaycatalog.mp.microsoft.com" ? "gamepass"
                : uri.AbsolutePath.Contains("IStoreQueryService/Query") || uri.PathAndQuery.Contains("GetTagList/v1/?language=english") ? "steam"
                : uri.Host == "catalog.gog.com" ? "gog"
                : uri.Host == "www.playstation.com" ? "psplus"
                : null;
            if (catalog is not null)
                _requests.AddOrUpdate(catalog, 1, (_, n) => n + 1);
            if (catalog == "gamepass" && HoldGamePass)
            {
                GamePassReached.TrySetResult();
                await _released.Task.WaitAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
