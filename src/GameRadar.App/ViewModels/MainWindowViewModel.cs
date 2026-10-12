using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Cache;
using GameRadar.Core.Epic;
using GameRadar.Core.Logging;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using GameRadar.Core.Updates;
using GameRadar.Core.Xbox;

namespace GameRadar.App.ViewModels;

// The window's view model: navigation, banners, the startup sequence and the
// wiring of the view models of each part of the app, which know nothing of it:
// Accounts, History, Catalogs, SourceFilter, Recommendations, NotInterested,
// AddGame, WhatsNew, Legal, Language, Privacy. They take what they need by their
// constructor and call back through the delegates and events wired here.
// "partial": the MVVM Toolkit source generator writes the other half of the
// class (property change notifications, command properties) at build time.
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly SettingsSaver _saver;
    private readonly UpdateChecker _updateChecker;
    private readonly ErrorLog _log;
    private readonly Action<Uri> _openBrowser;

    public MainWindowViewModel(
        HttpClient http,
        SettingsStore settingsStore,
        CacheStore cache,
        ImageFileCache imageCache,
        XboxAccount account,
        SteamAccount steamAccount,
        PlayStationAccount playStationAccount,
        EpicAccount epicAccount,
        Action<Uri> openBrowser,
        LocalDataEraser eraser,
        Action quit,
        ErrorLog log,
        Action restart,
        DataFolder? dataFolder = null)
    {
        // The children are created in this order, and it matters: several callbacks
        // below capture children created later (Recommendations, Catalogs, NotInterested)
        // and none may run before the end of this constructor, where Accounts.UpdateAll()
        // then History.Rebuild() come last. The window subscribes to a child's events once
        // everything the handler reads exists (Library for AccountChanged); the boxes
        // of SourceFilter are set from the settings before anyone listens, so that
        // setting them writes nothing back.
        _settingsStore = settingsStore;
        _updateChecker = new UpdateChecker(http);
        _openBrowser = openBrowser;
        _log = log;
        Notices = new NoticeCenter(log);
        // Before the accounts: the GOG profile name is a setting.
        _saver = new SettingsSaver(settingsStore, settingsStore.Load());
        // Before the accounts are read below: their changes refresh the platforms' lines.
        Accounts = new AccountsViewModel(http, cache, account, steamAccount, playStationAccount, epicAccount, _saver, openBrowser, Notices, OnHistoryFetchedAsync);
        WhatsNew = new WhatsNewViewModel(_saver, Show);
        Legal = new LegalViewModel(Show);
        SourceFilter = new SourceFilterViewModel(_saver);
        // Before the library, which reads its catalogs; its callbacks reach children created below.
        Catalogs = new CatalogsViewModel(http, cache, _saver, Notices, log, SourceFilter,
            OnCatalogsRebuildHistory, OnCatalogsChangedAsync, OnTitlesMoved);
        Library = new LibraryState(Accounts, _saver, Catalogs);
        Accounts.AccountChanged += RefreshRelativeTimes;
        History = new HistoryViewModel(Accounts, Library, _saver, SourceFilter, OnChoicesChanged);
        Recommendations = new RecommendationsViewModel(http, cache, imageCache, log, openBrowser, Library, Accounts, _saver, SourceFilter);
        SourceFilter.PlanChanged += RebuildAndRecompute;
        // The download is heard once the caches are read (InitializeAsync).
        SourceFilter.PsPlusPlanChanged += RebuildAndRecompute;
        NotInterested = new NotInterestedViewModel(_saver, OnNotInterestedChanged);
        History.Rebuilt += OnHistoryRebuilt;
        AddGame = new AddGameViewModel(http, Library, _saver, log, SourceFilter, Show, RebuildAndRecompute, Catalogs.GetPsPlusCatalogForSearchAsync);
        Language = new LanguageViewModel(_saver, Notices, restart);
        Privacy = new PrivacyViewModel(Notices, eraser, quit, openBrowser, IsBusyForErase, StopBackgroundWork);
        DataLocation = new DataLocationViewModel(dataFolder ?? DataFolder.Default, openBrowser);
        Accounts.UpdateAll();
        // The games added by hand are in the settings: shown at once, before
        // the synced histories are read from their caches.
        History.Rebuild();
    }

    // What the children call to open a page: the window owns the navigation.
    private void Show(AppPage page) => CurrentPage = page;

    /// <summary>The history page: its rows, the search, the sort and the filters.</summary>
    public HistoryViewModel History { get; }

    /// <summary>"Pas intéressé" on the cards, and the list of the refused games.</summary>
    public NotInterestedViewModel NotInterested { get; }

    // The refusals changed: the choice is saved, and the cards are rebuilt from
    // the last computation (a few milliseconds, no genre or tag lookup).
    private void OnNotInterestedChanged()
    {
        Recommendations.SaveFilter();
        Recommendations.RebuildSections();
    }

    /// <summary>The candidate catalogs, one line each: cache, download, status lines.</summary>
    public CatalogsViewModel Catalogs { get; }

    // What the catalogs call: children created after them (see the constructor).
    private void OnCatalogsRebuildHistory() => History.Rebuild();
    private Task OnCatalogsChangedAsync() => Recommendations.UpdateAsync();
    private void OnTitlesMoved() => NotInterested.Reload();

    /// <summary>The "Game Pass" and "Steam" boxes of "Pour toi", and the player's plan.</summary>
    public SourceFilterViewModel SourceFilter { get; }

    // Another plan (other candidates, other history badges) or a game added by hand:
    // the history is rebuilt, then the recommendations (genres and tags come from the cache).
    private void RebuildAndRecompute()
    {
        History.Rebuild();
        _ = Recommendations.SaveChoicesAndRecomputeAsync();
    }

    // Another PS Plus plan: the recommendations follow at once with the catalog we have
    // (RebuildAndRecompute), then the catalog is downloaded when there is none yet or it is
    // old (it is never downloaded for a player without a plan), and the recommendations
    // follow again. Heard once the startup has read the caches, like the PlayStation Store
    // box: before, no catalog is in memory yet, and the startup downloads it itself if stale.
    private async Task DownloadPsPlusIfNeededAsync()
    {
        if (Catalogs.PsPlus.IsWanted() && Catalogs.PsPlus.NeedsDownload(DateTimeOffset.UtcNow))
            await Catalogs.RefreshAsync([Catalogs.PsPlus]);
    }

    // The "PlayStation Store" box ticked (a click, or the rule once a PlayStation history
    // arrives): its catalog is downloaded when there is none yet or it is old; the
    // recommendations follow (the cards already follow the box).
    // Heard once the startup has read the caches (InitializeAsync): a box ticked by the rule
    // before, while the window is built, is downloaded by the startup itself if stale.
    // Its games are candidates only while the box is ticked: with a fresh catalog, recomputed.
    private async Task OnPlayStationStoreTickedAsync()
    {
        if (Catalogs.PsStore.NeedsDownload(DateTimeOffset.UtcNow))
            await Catalogs.RefreshAsync([Catalogs.PsStore]);
        else
            await Recommendations.UpdateAsync();
    }

    /// <summary>The "Pour toi" page: the computation, the cards and their pictures.</summary>
    public RecommendationsViewModel Recommendations { get; }

    /// <summary>The "Ajouter un jeu" page.</summary>
    public AddGameViewModel AddGame { get; }

    // A choice is in the settings (taste, finished, a game edited by hand): saved, then the recommendations follow.
    private void OnChoicesChanged() => _ = Recommendations.SaveChoicesAndRecomputeAsync();

    /// <summary>The release notes and their banner.</summary>
    public WhatsNewViewModel WhatsNew { get; }

    /// <summary>The terms of use and the licenses of the components.</summary>
    public LegalViewModel Legal { get; }

    /// <summary>The "Langue" setting.</summary>
    public LanguageViewModel Language { get; }

    /// <summary>Where to revoke the access, and "Effacer toutes mes données".</summary>
    public PrivacyViewModel Privacy { get; }
    public DataLocationViewModel DataLocation { get; }

    // Handed to the privacy page, for its "busy" message only: the stores
    // refuse to write once erased (EraseGuard), this just tells the player to
    // wait for a sync instead of erasing in the middle of it.
    private bool IsBusyForErase() =>
        Catalogs.IsRefreshing || Accounts.IsBusy || _saver.IsSaving;

    private void StopBackgroundWork() => Recommendations.Stop();

    // ============ Navigation ============

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPageIndex),
        nameof(IsRecommendationsPage), nameof(IsHistoryPage), nameof(IsSettingsPage), nameof(IsWhatsNewPage),
        nameof(IsSidePage))]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    public partial AppPage CurrentPage { get; set; }

    /// <summary>
    /// The current page as a position, for the SelectedIndex of the TabbedPage
    /// that holds the pages (in the enum's order). Its tabs are hidden: the
    /// sidebar buttons navigate, so only this view model changes the page.
    /// </summary>
    public int CurrentPageIndex
    {
        get => (int)CurrentPage;
        set
        {
            // -1 = no page, e.g. while the window is being built: keep the current one.
            if (Enum.IsDefined((AppPage)value))
                CurrentPage = (AppPage)value;
        }
    }

    // The sidebar buttons are lit by these (Classes.selected).
    public bool IsRecommendationsPage => CurrentPage == AppPage.Recommendations;
    public bool IsHistoryPage => CurrentPage == AppPage.History;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;
    public bool IsWhatsNewPage => CurrentPage == AppPage.WhatsNew;

    /// <summary>A page opened from another one ("Nouveautés", legal texts), left with "Retour".</summary>
    public bool IsSidePage => IsSide(CurrentPage);

    private static bool IsSide(AppPage page) => page is AppPage.WhatsNew or AppPage.Terms or AppPage.Licenses or AppPage.AddGame;

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;

    // The page a side page was opened from, where "Retour" leads: Settings
    // from "À propos", "Pour toi" from the news banner at startup.
    private AppPage _pageBeforeSidePage = AppPage.Recommendations;

    // Hook generated by the MVVM Toolkit, with the old value as well.
    partial void OnCurrentPageChanged(AppPage oldValue, AppPage newValue)
    {
        if (IsSide(newValue) && !IsSide(oldValue))
            _pageBeforeSidePage = oldValue;
    }

    // Only on a side page: Escape, bound on the whole window, does nothing elsewhere.
    [RelayCommand(CanExecute = nameof(IsSidePage))]
    private void GoBack() => CurrentPage = _pageBeforeSidePage;

    // ============ Sync state (sidebar) ============

    /// <summary>The catalogs and the games played, read by the history, the add-a-game search and the recommendations.</summary>
    public LibraryState Library { get; }

    /// <summary>The accounts, and the platforms they sync.</summary>
    public AccountsViewModel Accounts { get; }

    // New history is in: the lists, then the recommendations, once.
    private async Task OnHistoryFetchedAsync()
    {
        History.Rebuild();
        await Recommendations.UpdateAsync();
    }

    [ObservableProperty]
    public partial string CatalogSyncLabel { get; set; } = Strings.Sync_CatalogWaiting;

    /// <summary>
    /// Re-computes "il y a 2 h" labels. Called after each data change and
    /// periodically by the window, since time passes while the app is open.
    /// </summary>
    public void RefreshRelativeTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var platform in Accounts.SyncedPlatforms)
            platform.RefreshRelativeTime(now);
        if (Library.Catalog is not null)
            CatalogSyncLabel = Localization.Format(Strings.Sync_Catalog, RelativeTime.Format(Library.Catalog.FetchedAt, now));
    }

    // ============ Error banner ============

    /// <summary>The error banner, visible from any page until dismissed, and the error log.</summary>
    public NoticeCenter Notices { get; }

    // ============ Update banner ============

    /// <summary>"Version 1.0.0" in the About card (3 parts, like the release tags).</summary>
    public static string AppVersionLabel => Localization.Format(Strings.About_Version, AppInfo.Version.ToString(3));

    /// <summary>A newer release found on GitHub at startup, shown until dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateLabel))]
    public partial AvailableUpdate? AvailableUpdate { get; set; }

    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateLabel => AvailableUpdate is null
        ? string.Empty
        : Localization.Format(Strings.Update_Available, AvailableUpdate.Version.ToString(3), AppInfo.Version.ToString(3));

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (AvailableUpdate is not null)
            _openBrowser(AvailableUpdate.DownloadPage);
    }

    [RelayCommand]
    private void DismissUpdate() => AvailableUpdate = null;

    private async Task CheckForUpdateAsync()
    {
        // UpdateChecker never throws for network reasons: nothing to catch.
        // No ConfigureAwait(false): the property is set back on the UI thread.
        var update = await _updateChecker.CheckAsync(AppInfo.Version);
        if (update is not null)
            AvailableUpdate = update;
    }

    /// <summary>
    /// Shows the cached data, then refreshes the catalogs that are missing
    /// or too old. A constructor cannot be async, so the app calls this
    /// right after creating the view model.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Started first, awaited last: GitHub answers while the caches are
        // read and the catalogs refreshed, which can take minutes.
        Task updateCheck = CheckForUpdateAsync();

        // Reading and parsing the caches is synchronous work: moved to
        // background threads, all read at once (separate files), so the
        // window stays responsive; each await resumes here, on the UI thread.
        await Task.WhenAll(Accounts.LoadCachedAsync(), Catalogs.LoadCachedAsync());
        SourceFilter.PsPlusPlanChanged += () => _ = DownloadPsPlusIfNeededAsync();
        // Settings still saved with French titles (refusals, games added by
        // hand), moved to the English ones of the cached catalogs before
        // anything reads them; nothing to read once done.
        await Catalogs.MoveToEnglishTitlesAsync();
        History.Rebuild();
        // A 1.1.0 user may have no settings file, but has a cached history.
        await WhatsNew.CheckAsync(returningUser: _settingsStore.Exists || Accounts.SyncedPlatforms.Any(p => p.History is not null));
        Catalogs.ShowLoaded();

        // The catalogs cost no API quota: refresh them on our own when
        // needed, only the stale ones (the Game Pass download weighs ~47 MB), and
        // only those wanted (PS Plus: nothing is asked of playstation.com, nor
        // reported as an error, for someone without PS Plus).
        var now = DateTimeOffset.UtcNow;
        var stale = Catalogs.Lines.Where(l => l.IsWanted() && l.NeedsDownload(now)).ToList();
        SourceFilter.PlayStationStoreTicked += () => _ = OnPlayStationStoreTickedAsync();
        if (stale.Count > 0)
        {
            // A slow download (Game Pass, PlayStation Store): meanwhile the cached
            // catalogs, even old, recommend right away, and the new ones replace
            // them. The other catalogs alone (seconds) are just awaited.
            Task fromCache = stale.Any(l => l.SlowDownload) ? Recommendations.UpdateAsync() : Task.CompletedTask;
            await Catalogs.RefreshAsync(stale);
            await fromCache;
        }
        else
        {
            await Recommendations.UpdateAsync();
        }

        await updateCheck;
    }

    /// <summary>Completes once every settings save started so far is written (the tests cleaning up their folder).</summary>
    public Task WhenSettingsSavedAsync() => _saver.WhenSavedAsync();

    /// <summary>
    /// Stops the background work, then completes once nothing writes in the data folder
    /// anymore: settings saves, computations (their genre and tag lookups save what they
    /// found) and card pictures. For the tests, which then delete their folder.
    /// </summary>
    public Task WhenIdleAsync()
    {
        StopBackgroundWork();
        return Task.WhenAll(WhenSettingsSavedAsync(), Recommendations.WhenIdleAsync());
    }

    // The history rows were rebuilt: the figures drawn from them follow, in this order.
    private void OnHistoryRebuilt()
    {
        SourceFilter.ApplyPlayStationRule(Library.HasPlayStationHistory);
        RefreshRelativeTimes();
        Catalogs.UpdateGamePassStatus(History.GameCount, History.AllRows.Count(r => r.IsInGamePass));
    }
}
