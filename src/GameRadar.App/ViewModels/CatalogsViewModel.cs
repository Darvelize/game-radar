using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Cache;
using GameRadar.Core.Epic;
using GameRadar.Core.GOG;
using GameRadar.Core.GamePass;
using GameRadar.Core.Logging;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Services;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;

namespace GameRadar.App.ViewModels;

// The candidate catalogs (Game Pass, PS Plus, Steam and GOG stores), one line each:
// read from their cache at startup, downloaded again when stale or when the player
// asks, and their status lines in Settings › Data. LibraryState reads them.
public sealed partial class CatalogsViewModel : ObservableObject
{
    private readonly HttpClient _http;
    private readonly CacheStore _cache;
    private readonly SettingsSaver _saver;
    private readonly NoticeCenter _notices;
    private readonly ErrorLog _log;
    private readonly SourceFilterViewModel _filter;
    private readonly Action _rebuildHistory;
    private readonly Func<Task> _catalogsChanged;
    private readonly Action _titlesMoved;

    /// <param name="rebuildHistory">Rebuilds the history rows: their "Game Pass" badges come from the catalogs.</param>
    /// <param name="catalogsChanged">Called once the catalogs are refreshed, even after an error (the cached ones still recommend).</param>
    /// <param name="titlesMoved">The settings moved to English titles: the lists built from them are read again.</param>
    public CatalogsViewModel(
        HttpClient http,
        CacheStore cache,
        SettingsSaver saver,
        NoticeCenter notices,
        ErrorLog log,
        SourceFilterViewModel filter,
        Action rebuildHistory,
        Func<Task> catalogsChanged,
        Action titlesMoved)
    {
        _http = http;
        _cache = cache;
        _saver = saver;
        _notices = notices;
        _log = log;
        _filter = filter;
        _rebuildHistory = rebuildHistory;
        _catalogsChanged = catalogsChanged;
        _titlesMoved = titlesMoved;

        var gamePass = new GamePassCatalogSource(cache);
        var steam = new SteamCatalogSource(cache);
        var gog = new GogCatalogSource(cache);
        var epic = new EpicCatalogSource(cache);
        var psPlus = new PsPlusCatalogSource(cache);
        var psStore = new PlayStationStoreCatalogSource(cache);
        GamePass = new CatalogLine
        {
            Title = Strings.Common_GamePassCatalog,
            LogContext = "Game Pass catalog",
            FetchingStatus = Strings.Status_FetchingGamePass,
            LoadCached = gamePass.LoadCached,
            Refresh = () => gamePass.RefreshAsync(new GamePassClient(_http)),
            // ~47 MB.
            SlowDownload = true,
            ErrorMessage = ex => NoticeCenter.ServiceErrorMessage<GamePassException>(
                ex, Strings.Error_CatalogUnreachable, Strings.Error_CatalogSlow, Strings.Error_CatalogFormat)
            // No Describe: UpdateGamePassStatus writes its line once the history is built.
        };
        PsPlus = new CatalogLine
        {
            Title = Strings.Common_PsPlusCatalog,
            LogContext = "PS Plus catalog",
            FetchingStatus = Strings.Status_FetchingPsPlus,
            NotLoadedStatus = Strings.Status_PsPlusCatalogNotLoaded,
            LoadCached = psPlus.LoadCached,
            Refresh = () => psPlus.RefreshAsync(new PsPlusCatalogClient(_http)),
            ErrorMessage = ex => NoticeCenter.ServiceErrorMessage<PlayStationException>(
                ex, Strings.Error_PsPlusUnreachable, Strings.Error_PsPlusSlow, Strings.Error_PsPlusFormat),
            Describe = c => Localization.Format(Strings.Status_PsPlusCatalog, c.Games.Count, c.FetchedAt.ToLocalTime()),
            // Nothing is asked of playstation.com (nor reported as an error) for someone without PS Plus.
            IsWanted = () => _filter.HasPsPlusPlan,
            // Its games carry no history badge.
            RebuildsHistory = false
        };
        Steam = new CatalogLine
        {
            Title = Strings.Common_SteamCatalog,
            LogContext = "Steam store",
            FetchingStatus = Strings.Status_FetchingSteamCatalog,
            NotLoadedStatus = Strings.Status_SteamCatalogNotLoaded,
            LoadCached = steam.LoadCached,
            Refresh = () => steam.RefreshAsync(new SteamCatalogClient(_http)),
            ErrorMessage = ex => NoticeCenter.ServiceErrorMessage<SteamException>(
                ex, Strings.Error_SteamStoreUnreachable, Strings.Error_SteamStoreSlow, Strings.Error_SteamStoreFormat),
            Describe = c => Localization.Format(Strings.Status_SteamCatalog, c.Games.Count, c.FetchedAt.ToLocalTime())
        };
        Gog = new CatalogLine
        {
            Title = Strings.Common_GogCatalog,
            LogContext = "GOG store",
            FetchingStatus = Strings.Status_FetchingGogCatalog,
            NotLoadedStatus = Strings.Status_GogCatalogNotLoaded,
            LoadCached = gog.LoadCached,
            Refresh = () => gog.RefreshAsync(new GogCatalogClient(_http)),
            ErrorMessage = ex => NoticeCenter.ServiceErrorMessage<GogException>(
                ex, Strings.Error_GogStoreUnreachable, Strings.Error_GogStoreSlow, Strings.Error_GogStoreFormat),
            Describe = c => Localization.Format(Strings.Status_GogCatalog, c.Games.Count, c.FetchedAt.ToLocalTime())
        };
        Epic = new CatalogLine
        {
            Title = Strings.Common_EpicCatalog,
            LogContext = "Epic store",
            FetchingStatus = Strings.Status_FetchingEpicCatalog,
            NotLoadedStatus = Strings.Status_EpicCatalogNotLoaded,
            LoadCached = epic.LoadCached,
            Refresh = () => epic.RefreshAsync(new EpicCatalogClient(_http)),
            ErrorMessage = ex => NoticeCenter.ServiceErrorMessage<EpicException>(
                ex, Strings.Error_EpicStoreUnreachable, Strings.Error_EpicStoreSlow),
            Describe = c => Localization.Format(Strings.Status_EpicCatalog, c.Games.Count, c.FetchedAt.ToLocalTime())
        };
        PsStore = new CatalogLine
        {
            Title = Strings.Common_PsStoreCatalog,
            LogContext = "PlayStation Store",
            FetchingStatus = Strings.Status_FetchingPsStoreCatalog,
            NotLoadedStatus = Strings.Status_PsStoreCatalogNotLoaded,
            LoadCached = psStore.LoadCached,
            Refresh = () => psStore.RefreshAsync(new PlayStationStoreClient(_http)),
            ErrorMessage = ex => NoticeCenter.ServiceErrorMessage<PlayStationException>(
                ex, Strings.Error_PsStoreUnreachable, Strings.Error_PsStoreSlow),
            Describe = c => Localization.Format(Strings.Status_PsStoreCatalog, c.Games.Count, c.FetchedAt.ToLocalTime()),
            // Nothing is asked of the store while its box is unticked.
            IsWanted = () => _filter.ShowPlayStationStore,
            // Its games carry no history badge.
            RebuildsHistory = false,
            // One request per game: ~26 s.
            SlowDownload = true
        };
        Lines = [GamePass, PsPlus, Steam, Gog, Epic, PsStore];
        // The PS Plus block comes and goes with the plan, the PlayStation Store one with its box.
        _filter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SourceFilterViewModel.HasPsPlusPlan) or nameof(SourceFilterViewModel.ShowPlayStationStore))
                OnPropertyChanged(nameof(ShownLines));
        };
    }

    public CatalogLine GamePass { get; }

    public CatalogLine PsPlus { get; }

    public CatalogLine Steam { get; }

    public CatalogLine Gog { get; }

    public CatalogLine Epic { get; }

    public CatalogLine PsStore { get; }

    /// <summary>Every catalog, in the order of Settings › Data. A new catalog is one more entry.</summary>
    public IReadOnlyList<CatalogLine> Lines { get; }

    /// <summary>
    /// The blocks of Settings › Data: the catalogs wanted (PS Plus only with a plan).
    /// A list rather than hidden items, which would keep their spacing.
    /// </summary>
    public IReadOnlyList<CatalogLine> ShownLines => [.. Lines.Where(l => l.IsWanted())];

    /// <summary>True while a catalog is being fetched, from the button or at startup.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshAllCommand))]
    public partial bool IsRefreshing { get; set; }

    private bool CanRefreshAll() => !IsRefreshing;

    // The refreshes running (see RefreshAsync).
    private int _refreshes;

    /// <summary>The button: every catalog, whatever its age (PS Plus only for a player who has a plan).</summary>
    [RelayCommand(CanExecute = nameof(CanRefreshAll))]
    private Task RefreshAllAsync() => RefreshAsync(Lines.Where(l => l.IsWanted()));

    /// <summary>
    /// Reads the catalogs from their cache, all at once (separate files):
    /// Task.Run moves the synchronous reading and parsing to background
    /// threads, so the window stays responsive; the await resumes on the UI thread.
    /// </summary>
    public Task LoadCachedAsync() =>
        Task.WhenAll(Lines.Select(async line => line.Catalog = await Task.Run(line.LoadCached)));

    /// <summary>The status lines of the catalogs read from their cache (Game Pass: see <see cref="UpdateGamePassStatus"/>).</summary>
    public void ShowLoaded()
    {
        foreach (var line in Lines)
            line.ShowLoaded();
    }

    /// <summary>
    /// Fetches these catalogs, all at once and each on its own (a Steam error
    /// keeps the Game Pass recommendations), and each one counts as soon as it
    /// arrives: GOG does not wait for the ~47 MB of the Game Pass.
    /// </summary>
    public async Task RefreshAsync(IEnumerable<CatalogLine> lines)
    {
        var chosen = lines.ToList();
        // Each recomputation cancels the one before; all are awaited at the end,
        // after the button came back (a recomputation may look up tags for minutes).
        var recomputations = new List<Task>();
        bool[] fetched;

        // No ConfigureAwait(false): each line comes back on the UI thread to update itself.
        async Task<bool> FetchAndShowAsync(CatalogLine line)
        {
            if (!await line.FetchAsync(_notices))
                return false;
            // Status lines rewritten only after a success: an error stays readable.
            if (line.RebuildsHistory)
                _rebuildHistory();
            recomputations.Add(_catalogsChanged());
            return true;
        }

        // Refreshes may overlap (a PS Plus plan chosen during the startup one):
        // the button comes back once the last one ends.
        _refreshes++;
        IsRefreshing = true;
        try
        {
            fetched = await Task.WhenAll(chosen.Select(FetchAndShowAsync));
        }
        finally
        {
            IsRefreshing = --_refreshes > 0;
        }

        // Settings saved with the former French titles (refusals, games added
        // by hand): moved once to the English titles of the new catalogs.
        if (await MoveToEnglishTitlesAsync())
        {
            _rebuildHistory();
            recomputations.Add(_catalogsChanged());
        }
        // Even after errors only: the cached catalogs still make recommendations.
        else if (!fetched.Any(f => f))
        {
            recomputations.Add(_catalogsChanged());
        }
        await Task.WhenAll(recomputations);
    }

    /// <summary>
    /// Moves the settings saved with French titles to the English ones
    /// (<see cref="EnglishTitlesMigration"/>). The former French catalogs are
    /// deleted only once the settings are saved: after a failed save (or a
    /// crash), they are still there and the move runs again at the next start.
    /// Returns true when the settings changed.
    /// </summary>
    public async Task<bool> MoveToEnglishTitlesAsync()
    {
        // Read once for both steps; nothing at all once the move is done.
        var former = EnglishTitlesMigration.ReadFormerCatalogs(_cache, GamePass.Catalog, Steam.Catalog);
        if (former.Count == 0)
            return false;

        bool changed = EnglishTitlesMigration.Apply(_saver.Settings, former);
        try
        {
            if (changed)
            {
                _titlesMoved();
                await _saver.SaveAsync();
            }
            await EnglishTitlesMigration.TrimFormerCatalogsAsync(_saver.Settings, _cache, former);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("Settings", "Saving the settings moved to English titles failed", ex);
        }
        return changed;
    }

    /// <summary>
    /// The Game Pass line: how many games the player's plan has, and how many
    /// of the games the player has played are in it.
    /// </summary>
    public void UpdateGamePassStatus(int playedGames, int inGamePass)
    {
        // A failed download keeps its error readable: another catalog's success
        // rebuilds the history, which must not write the old catalog over it.
        if (GamePass.Catalog is not { } catalog || GamePass.Failed)
            return;

        // The player's plan first: that is what the recommendations use.
        int inPlan = catalog.Games.Count(g => g.ForPlan(_filter.GamePassPlan).InGamePass);
        string inYourPlan = playedGames == 0
            ? Localization.Format(Strings.Catalog_InPlan, inPlan, _filter.GamePassLabel)
            : Localization.Format(Strings.Catalog_InPlanPlayed, inPlan, _filter.GamePassLabel, inGamePass);
        // Ultimate has every game: the total would only repeat the same number.
        string counts = !_filter.HasGamePassPlan ? Localization.Format(Strings.Catalog_All, catalog.Games.Count)
            : inPlan == catalog.Games.Count ? inYourPlan
            : Localization.Format(Strings.Catalog_OfAllPlans, inYourPlan, catalog.Games.Count);
        GamePass.Status = Localization.Format(Strings.Catalog_Status, counts, catalog.FetchedAt.ToLocalTime());
    }

    /// <summary>
    /// The PS Plus catalog for the search of "Ajouter un jeu" when the PlayStation Store is in trouble:
    /// the loaded one, else the one on disk (whatever its age: a game name does not go stale), else
    /// downloaded once. The player's own search is what asks for it, so this is the one case where a
    /// player without a plan reaches playstation.com; it is then kept on disk. Either is kept loaded,
    /// as the next start would (without a plan, its games are never recommended): read once, not at
    /// each search. Null when none can be had.
    /// Nothing is shown on the status line or the banner: the search says the store is down.
    /// </summary>
    public async Task<GameCatalog?> GetPsPlusCatalogForSearchAsync()
    {
        if (PsPlus.Catalog is { } loaded)
            return loaded;
        try
        {
            PsPlus.Catalog = await Task.Run(PsPlus.LoadCached) ?? await PsPlus.Refresh();
            // Its block in Settings › Data says so (shown once a plan is chosen).
            PsPlus.ShowLoaded();
            return PsPlus.Catalog;
        }
        catch (Exception ex) when (StoreLookup.Failed(ex, CancellationToken.None))
        {
            _log.Write(PsPlus.LogContext, "Could not download the PS Plus catalog for the search", ex);
            return null;
        }
    }
}
