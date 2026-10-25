using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.GamePass;
using GameRadar.Core.Logging;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.App.ViewModels;

// The "Ajouter un jeu" page: the player types a title, picks the game among
// the Game Pass catalog and the Steam store (or keeps it as typed), says
// where they played it and how far they went. Several games in a row.
public sealed partial class AddGameViewModel : ObservableObject
{
    private readonly HttpClient _http;
    private readonly LibraryState _library;
    private readonly SettingsSaver _saver;
    private readonly ErrorLog _log;
    private readonly SourceFilterViewModel _filter;
    private readonly Action<AppPage> _showPage;
    private readonly Action _gameAdded;
    private readonly Func<Task<GameCatalog?>> _psPlusCatalog;

    /// <param name="filter">Gives the player's plan: a game outside it is not offered as Game Pass.</param>
    /// <param name="showPage">Shows a page of the window (the window owns the navigation).</param>
    /// <param name="gameAdded">Called once a game is in the settings: the window rebuilds its lists and recomputes.</param>
    /// <param name="psPlusCatalog">The PS Plus catalog, whatever the player's plan, when the PlayStation Store cannot be searched.</param>
    public AddGameViewModel(
        HttpClient http,
        LibraryState library,
        SettingsSaver saver,
        ErrorLog log,
        SourceFilterViewModel filter,
        Action<AppPage> showPage,
        Action gameAdded,
        Func<Task<GameCatalog?>> psPlusCatalog)
    {
        _http = http;
        _library = library;
        _saver = saver;
        _log = log;
        _filter = filter;
        _showPage = showPage;
        _gameAdded = gameAdded;
        _psPlusCatalog = psPlusCatalog;
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>What the player typed in the search box.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>The games found, then the title as typed.</summary>
    public ObservableCollection<AddGameResultRow> Results { get; } = [];

    /// <summary>Follows the collection itself (CollectionChanged, see the constructor).</summary>
    public bool HasResults => Results.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial AddGameResultRow? SelectedResult { get; set; }

    /// <summary>
    /// A game found only in the PlayStation Store (or PS Plus) was played on PlayStation, one found only
    /// in the Microsoft Store on Xbox: the platform is preset, and stays changeable. A game that Steam
    /// or the Game Pass also sells may have been played anywhere: the player's choice stays.
    /// </summary>
    partial void OnSelectedResultChanged(AddGameResultRow? value)
    {
        if (value is null || value.IsAsTyped || value.OnSteam || value.InGamePass)
            return;
        if (value.OnPlayStation && !value.OnXbox)
            Platform = ManualGameOptions.Of(ManualPlatform.PlayStation);
        else if (value.OnXbox && !value.OnPlayStation)
            Platform = ManualGameOptions.Of(ManualPlatform.OldXbox);
    }

    public IReadOnlyList<ManualPlatformOption> Platforms => ManualGameOptions.Platforms;
    public IReadOnlyList<ManualLevelOption> Levels => ManualGameOptions.Levels;

    // Kept from one game to the next: a player adds their PlayStation games in a row.
    [ObservableProperty]
    public partial ManualPlatformOption Platform { get; set; } = ManualGameOptions.Platforms[0];

    [ObservableProperty]
    public partial ManualLevelOption Level { get; set; } = ManualGameOptions.Of(ManualLevel.PlayedALot);

    private int _search;

    /// <summary>"Recherche…", nothing found, Steam unreachable; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>"« X » est ajouté à ton historique", after an addition.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    public partial string? Confirmation { get; set; }

    public bool HasConfirmation => Confirmation is not null;

    [RelayCommand]
    private void Open()
    {
        Confirmation = null;
        _showPage(AppPage.AddGame);
    }

    /// <summary>
    /// The Game Pass catalog (already loaded, instant), then Steam's search
    /// (1 request, no key). Steam unreachable: the catalog's games still come.
    /// A new search while one waits for the stores runs too (Enter, the button):
    /// the numbering below leaves the older answer unused.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        string typed = Text.Trim();
        if (typed.Length == 0)
            return;

        // Numbered: a search overtaken by a newer one, or by an addition
        // that cleared the page, leaves its answer unused.
        int search = ++_search;
        Confirmation = null;
        Status = Strings.AddGame_Searching;
        // Seen by the player's plan: a game outside it is not offered as Game Pass.
        var plan = _filter.GamePassPlan;
        IReadOnlyList<ManualGameMatch> fromCatalog = _library.Catalog is null ? []
            : ManualGameSearch.InCatalog(_library.Catalog.Games.Select(g => g.ForPlan(plan)), typed);
        // The three stores at once, each one failing alone: what the others found still comes.
        var steamSearch = SearchStoreAsync(() => new SteamTagClient(_http).SuggestGamesAsync(typed), Strings.AddGame_SteamUnreachable);
        var xboxSearch = SearchStoreAsync(() => new GamePassClient(_http).SuggestGamesAsync(typed), Strings.AddGame_XboxUnreachable);
        var playStationSearch = SearchStoreAsync(() => new PlayStationStoreClient(_http).SearchGamesAsync(typed), Strings.AddGame_PlayStationUnreachable);
        var (fromSteam, steamStatus) = await steamSearch;
        var (fromXbox, xboxStatus) = await xboxSearch;
        var (fromPlayStation, playStationStatus) = await playStationSearch;
        string status = string.Join(" ", new[] { steamStatus, xboxStatus, playStationStatus }.Where(s => s.Length > 0));

        // The PS Plus catalog is searched whatever the PlayStation Store says. Store answering: a
        // complement, with the player's plan (without one, nothing: the catalog is not even downloaded).
        // Store in trouble (the site changed, no answer): the fallback, and then the HIGHEST plan, whatever
        // the player has (user's choice): better a game of a plan they do not have than none.
        IReadOnlyList<ManualGameMatch> fromPsPlus = [];
        if (playStationStatus.Length > 0)
        {
            if (await _psPlusCatalog() is { } fallback)
                fromPsPlus = ManualGameSearch.InPsPlusCatalog(
                    fallback.Games.Select(g => g.ForPsPlusPlan(PsPlusTier.Premium)).Where(g => g.InPsPlus), typed);
        }
        else if (_filter.HasPsPlusCatalog && _library.PsPlusCatalog is { } psPlus)
        {
            fromPsPlus = ManualGameSearch.InPsPlusCatalog(
                psPlus.Games.Select(g => g.ForPsPlusPlan(_filter.PsPlusPlan)).Where(g => g.InPsPlus), typed);
        }

        if (search != _search)
            return;

        var matches = ManualGameSearch.Merge(fromCatalog, fromSteam, fromXbox, fromPsPlus, fromPlayStation);
        Results.Clear();
        foreach (var match in matches)
            Results.Add(new AddGameResultRow(match));
        // Always there when it is not a result already: a game found nowhere
        // (an old console game) can still be added.
        if (!matches.Any(m => GameTitle.Key(m.Title) == GameTitle.Key(typed)))
            Results.Add(AddGameResultRow.AsTyped(typed));

        Status = status.Length > 0 ? status : matches.Count == 0 ? Strings.AddGame_NoResult : string.Empty;
        SelectedResult = Results.FirstOrDefault();
    }

    /// <summary>One store's search: its games, or none and the sentence to show when it fails (logged).</summary>
    private async Task<(IReadOnlyList<T> Games, string Status)> SearchStoreAsync<T>(Func<Task<IReadOnlyList<T>>> search, string unreachable)
    {
        try
        {
            return (await search(), string.Empty);
        }
        catch (Exception ex) when (StoreLookup.Failed(ex, CancellationToken.None))
        {
            _log.Write("Add a game", unreachable, ex);
            return ([], unreachable);
        }
    }

    /// <summary>Adds the chosen game (or updates it: the same game replaces itself), then clears the page for the next one.</summary>
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        if (SelectedResult is not { } result)
            return;

        // The genres of a game found nowhere else: the catalog lists them (PS Plus), or the
        // PlayStation Store is asked, once, for this game only. Nothing else is awaited, so
        // any other game is added at once.
        var genres = result.Genres ?? await FetchPlayStationGenresAsync(result);

        var manualGames = _saver.Settings.ManualGames;
        var game = new ManualGame(result.Title, Platform.Platform, Level.Level, result.SteamAppId, genres);
        int existing = manualGames.FindIndex(g => g.Id == game.Id);
        if (existing >= 0)
            manualGames[existing] = game;
        else
            manualGames.Add(game);
        Confirmation = Localization.Format(existing >= 0 ? Strings.AddGame_Updated : Strings.AddGame_Added, game.Title);

        _gameAdded();

        _search++;
        Text = string.Empty;
        Results.Clear();
        SelectedResult = null;
        Status = string.Empty;
    }

    private bool CanAdd() => SelectedResult is not null;

    /// <summary>
    /// The genres of a game found only in the PlayStation Store (1 request); a game that Steam, the
    /// Game Pass or the Microsoft Store also knows has its genres looked up by name or app id, and
    /// asks nothing here. A failure adds the game without genres, never refuses it.
    /// </summary>
    private async Task<IReadOnlyList<string>?> FetchPlayStationGenresAsync(AddGameResultRow result)
    {
        if (result.PlayStationProductId is not { } productId || result.SteamAppId is not null || result.InGamePass || result.OnXbox)
            return null;

        try
        {
            var genres = await new PlayStationStoreClient(_http).FetchGenresAsync(productId);
            return genres.Count > 0 ? genres : null;
        }
        catch (Exception ex) when (StoreLookup.Failed(ex, CancellationToken.None))
        {
            _log.Write("Add a game", "Could not read the genres of the PlayStation Store", ex);
            return null;
        }
    }
}

/// <summary>A line of the results of "Ajouter un jeu".</summary>
public sealed class AddGameResultRow
{
    public AddGameResultRow(ManualGameMatch match)
    {
        Title = match.Title;
        Label = match.Title;
        SteamAppId = match.SteamAppId;
        InGamePass = match.InGamePass;
        OnSteam = match.OnSteam;
        OnXbox = match.OnXbox;
        OnPlayStation = match.OnPlayStation;
        PlayStationProductId = match.PlayStationProductId;
        Genres = match.Genres;
    }

    private AddGameResultRow(string typed)
    {
        Title = typed;
        Label = Localization.Format(Strings.AddGame_AsTyped, typed);
        IsAsTyped = true;
    }

    /// <summary>The title as typed, found nowhere: added without genres.</summary>
    public static AddGameResultRow AsTyped(string typed) => new(typed);

    /// <summary>The title saved.</summary>
    public string Title { get; }

    /// <summary>What the line shows.</summary>
    public string Label { get; }

    public string? SteamAppId { get; }
    public bool InGamePass { get; }
    public bool OnSteam { get; }
    public bool OnXbox { get; }

    /// <summary>The "Xbox" badge: a Game Pass game already says Xbox with its own badge.</summary>
    public bool ShowXboxBadge => OnXbox && !InGamePass;

    public bool OnPlayStation { get; }

    /// <summary>Its PlayStation Store product id (asked its genres when it is added), when found there.</summary>
    public string? PlayStationProductId { get; }

    /// <summary>Its genres when the catalog it comes from lists them.</summary>
    public IReadOnlyList<string>? Genres { get; }

    public bool ShowPlayStationBadge => OnPlayStation;
    public bool IsAsTyped { get; }
}
