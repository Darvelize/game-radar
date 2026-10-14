using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Logging;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;

namespace GameRadar.App.ViewModels;

// The "Pour toi" page: the recommendations computed from the histories and the
// catalogs (genres and Steam tags looked up and cached), the cards on screen,
// and their pictures. Switching a box or refusing a game only rebuilds the
// cards from the last computation.
public sealed partial class RecommendationsViewModel : ObservableObject
{
    private readonly HttpClient _http;
    private readonly ImageFileCache _imageCache;
    private readonly ErrorLog _log;
    private readonly Action<Uri> _openBrowser;
    private readonly LibraryState _library;
    private readonly AccountsViewModel _accounts;
    private readonly SettingsSaver _saver;
    private readonly SourceFilterViewModel _filter;
    private readonly StoreGenreSource _genreSource;
    private readonly SteamGenreSource _steamGenreSource;
    private readonly SteamTagSource _tagSource;
    private readonly SteamWishlistSource _wishlistSource;
    private readonly BackgroundWork _work;

    // Cancels a recommendation computation made obsolete by newer data.
    private CancellationTokenSource? _recommendationsCts;

    public RecommendationsViewModel(
        HttpClient http,
        CacheStore cache,
        ImageFileCache imageCache,
        ErrorLog log,
        Action<Uri> openBrowser,
        LibraryState library,
        AccountsViewModel accounts,
        SettingsSaver saver,
        SourceFilterViewModel filter,
        BackgroundWork work)
    {
        _work = work;
        _http = http;
        _imageCache = imageCache;
        _log = log;
        _openBrowser = openBrowser;
        _library = library;
        _accounts = accounts;
        _saver = saver;
        _filter = filter;
        _genreSource = new StoreGenreSource(cache);
        _steamGenreSource = new SteamGenreSource(cache);
        _tagSource = new SteamTagSource(cache);
        _wishlistSource = new SteamWishlistSource(cache);
        // The boxes were set from the settings before this subscription: nothing was heard then.
        filter.SaveRequested += SaveFilter;
        filter.Changed += RebuildSections;
    }

    /// <summary>Stops what could still write a file (Steam tags saved every 50 games, card pictures).</summary>
    public void Stop()
    {
        _recommendationsCts?.Cancel();
        _cardImagesCts?.Cancel();
    }

    /// <summary>"Parce que tu as aimé X" sections, then the genre-based picks.</summary>
    public ObservableCollection<RecommendationSectionRow> Sections { get; } = [];

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsComputing { get; set; }

    /// <summary>The Steam box only makes sense once Steam store candidates are loaded.</summary>
    [ObservableProperty]
    public partial bool HasSteamCandidates { get; set; }

    /// <summary>The GOG box only makes sense once GOG store candidates are loaded.</summary>
    [ObservableProperty]
    public partial bool HasGogCandidates { get; set; }

    /// <summary>The Epic box only makes sense once Epic store candidates are loaded.</summary>
    [ObservableProperty]
    public partial bool HasEpicCandidates { get; set; }

    /// <summary>A click on a card: the game's page on the Xbox store or on Steam.</summary>
    [RelayCommand]
    private void OpenPage(Uri page) => _openBrowser(page);

    // Shown on the recommendations page only: no banner.
    private void ReportRecommendationError(string message, Exception ex)
    {
        Status = message;
        _log.Write("Recommendations", message, ex);
    }

    /// <summary>
    /// Computes the recommendations from the current histories and catalog.
    /// May look up the genres of new played games in the Microsoft Store (and
    /// the Steam store). Never throws: errors end up in <see cref="Status"/>.
    /// </summary>
    // Kept until it ends: a replaced computation still saves the genres and tags it found.
    public Task UpdateAsync() => _work.Track(ComputeAsync());

    private async Task ComputeAsync()
    {
        // Any catalog is enough: the Game Pass download may fail while the
        // Steam store loads (and the other way round).
        if (!_library.HasAnyHistory
            || !_library.HasCatalog)
        {
            // Nothing to recommend from (the last game removed): the running
            // computation and the cards of that history go too, or a filter
            // click would show them again.
            _recommendationsCts?.Cancel();
            _recommendationsCts = null;
            _recommendationState = null;
            ClearCards();
            IsComputing = false;
            Status = Strings.Reco_Waiting;
            return;
        }

        // A newer computation replaces any running one.
        _recommendationsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _recommendationsCts = cts;

        var gamePassGames = _library.Catalog?.Games ?? [];
        var steamGames = _library.SteamCatalog?.Games ?? [];
        var psPlusGames = _library.PsPlusCatalog?.Games ?? [];
        var gogGames = _library.GogCatalog?.Games ?? [];
        var epicGames = _library.EpicCatalog?.Games ?? [];
        // Only while its box is ticked: its games, mostly PlayStation exclusives, would
        // otherwise cost Steam tag searches for cards never shown.
        var psStoreGames = _filter.ShowPlayStationStore ? _library.PsStoreCatalog?.Games ?? [] : [];
        var plan = _filter.GamePassPlan;
        var psPlusPlan = _filter.PsPlusPlan;
        // Copies: the background computation must not read the sets while
        // a click on the UI thread modifies them (HashSet is not thread-safe).
        var excluded = new HashSet<string>(_saver.Settings.ExcludedFromTaste);
        var included = new HashSet<string>(_saver.Settings.IncludedInTaste);
        // Manual "finished" marks applied before anything else, so both the
        // taste profile and the status counts see the final statuses.
        var playedGames = PlayStatusOverrides.Apply(_library.CombinedHistory(), new HashSet<string>(_saver.Settings.MarkedCompleted));
        IsComputing = true;
        Status = Strings.Reco_Analyzing;

        try
        {
            var matcher = new CatalogMatcher(gamePassGames);
            var store = new GamePassClient(_http);

            // Only by the newest computation: a replaced one would share tags of old data.
            void ShareTags(SteamTagIndex index)
            {
                cts.Token.ThrowIfCancellationRequested();
                _library.SetTags(index, _tagNames);
            }

            // Every candidate: the Game Pass and PS Plus games of the player's plans,
            // and the Steam, GOG, Epic and PlayStation Store ones.
            var pool = CatalogMerger.Combine(
                gamePassGames.Select(g => g.ForPlan(plan)).ToList(), steamGames,
                psPlusGames.Select(g => g.ForPsPlusPlan(psPlusPlan)).ToList(), gogGames, epicGames, psStoreGames);
            var tagClient = new SteamTagClient(_http);
            // Tag names in the language of the app, for the explanations (1 request a month at most).
            _tagNames = await _tagSource.GetTagNamesAsync(tagClient, Localization.SteamLanguage, cts.Token);
            // The tags already in the cache, for the history right away (the
            // genre lookups below may take minutes the first time).
            var index = _tagSource.LoadIndex(pool, _saver.Settings.AddedTags);
            ShareTags(index);

            // Progress<T> is created here, on the UI thread: each Report()
            // made from the background work is posted back to this thread.
            var xboxProgress = new Progress<(int Done, int Total)>(p =>
                Status = Localization.Format(Strings.Reco_GenresXbox, p.Done, p.Total));
            var steamProgress = new Progress<(int Done, int Total)>(p =>
                Status = Localization.Format(Strings.Reco_GenresSteam, p.Done, p.Total));

            var genresFound = (await _genreSource.ResolveAsync(
                    playedGames.Where(g => g.Platform == XboxLiveClient.PlatformName), matcher, store, xboxProgress, cts.Token))
                .Concat(await _steamGenreSource.ResolveAsync(
                    // The source picks the games it finds by name (Steam, PlayStation, added by hand).
                    playedGames, matcher, store,
                    new SteamStoreClient(_http), steamProgress, cts.Token))
                .GroupBy(g => g.Game.Key)
                .ToDictionary(g => g.Key, g => g.First());
            // Every game with a taste signal, its genres when found: a game
            // without genres may still have Steam tags. Found nowhere (a
            // PlayStation exclusive): the platform's own genres, coarse.
            // Keys are unique across platforms (they start with the platform's name).
            var platformGenres = _accounts.SyncedPlatforms
                .SelectMany(p => p.History?.PlatformGenres ?? new Dictionary<string, IReadOnlyList<string>>())
                .ToDictionary(e => e.Key, e => e.Value);
            // The games added by hand that were found in the PlayStation Store or the PS Plus
            // catalog bring theirs, kept when they were added.
            foreach (var (key, genres) in ManualGames.PlatformGenres(_saver.Settings.ManualGames))
                platformGenres[key] = genres;
            var withGenres = playedGames
                .Where(g => g.HasEngagementSignal)
                .Select(g => genresFound.GetValueOrDefault(g.Key)
                    ?? new PlayedGameWithGenres(g, platformGenres.GetValueOrDefault(g.Key) ?? []))
                .ToList();

            // Pure computation, but on ~2,000 games: kept off the UI thread.
            // The Steam tags known in "index" make the similarity finer.
            Task<RecommendationState> Compute(SteamTagIndex index) => Task.Run(() =>
            {
                var now = DateTimeOffset.UtcNow;
                var tagged = pool.Select(g => g with { Tags = index.For(g) }).ToList();
                // A game's tags, the same on each of its platforms: the player's,
                // then Steam's from whichever platform Steam knows.
                var gameTags = withGenres
                    .Select(g => g.Game)
                    .GroupBy(g => GameTitle.Key(g.Name))
                    .ToDictionary(group => group.Key, group => index.ForGame([.. group]));
                // The games that count, once each: a game played on Xbox and
                // on Steam weighs as one, at its best level.
                // Known by their Steam tags, else by their genres: a game with neither says nothing.
                var tasteGames = HistoryMerger.KeepBestPerGame(
                    withGenres
                        .Where(g => TasteSelection.Counts(g.Game, excluded, included))
                        .Select(g => g with { Tags = gameTags[GameTitle.Key(g.Game.Name)] })
                        .Where(g => g.Genres.Count > 0 || g.Tags is not null), now);
                var taste = TasteProfile.Build(tasteGames, now);
                // Tag rarity measured on every candidate, so the filter does not change it.
                var vectorizer = new TagVectorizer(tagged.Select(g => g.Tags));
                // v1: filtered, deduplicated, scored (on tags, else genres);
                // then the v2 sections per favorite on top of them.
                var candidates = RecommendationEngine.Recommend(taste, tagged, playedGames, matcher, maxCount: int.MaxValue, tagVectorizer: vectorizer,
                    wishlist: _wishlistSource.LoadCached());
                // For the status line: games left out by the rule (barely
                // played), counted once per game, not per platform.
                int barelyPlayed = withGenres
                    .GroupBy(g => GameTitle.Key(g.Game.Name))
                    .Count(group => group.All(g => !TasteSelection.Counts(g.Game, excluded, included))
                        && group.Any(g => !excluded.Contains(g.Game.Key) && TasteProfile.IsBarelyPlayed(g.Game)));
                return new RecommendationState(
                    tasteGames, taste, candidates, excluded.Count, barelyPlayed,
                    Enum.GetValues<CatalogSource>().Where(source => pool.Any(g => g.IsIn(source))).ToHashSet(), vectorizer);
            }, cts.Token);

            void Show(RecommendationState state)
            {
                // Replaced meanwhile: Task.Run does not stop a started
                // computation, whose result is now old.
                cts.Token.ThrowIfCancellationRequested();
                _recommendationState = state;
                // The sections (a few ms) with the choices of now: a filter
                // click or a "Pas intéressé" made while it computed (the first
                // tag lookup takes minutes) holds.
                RebuildSections();
            }

            // First with the tags already known: shown right away.
            Show(await Compute(index));

            // Then the missing tags (a few minutes the first time), and again. Steam asked
            // nothing (none missing, or paused after a rate limit): the same tags, no second computation.
            var tagProgress = new Progress<(int Done, int Total)>(p =>
                Status = Localization.Format(Strings.Reco_Tags, p.Done, p.Total));
            if (await _tagSource.FetchMissingAsync(playedGames, pool, tagClient, tagProgress, cts.Token))
            {
                var found = _tagSource.LoadIndex(pool, _saver.Settings.AddedTags);
                ShareTags(found);
                Show(await Compute(found));
            }
        }
        // Replaced by a newer computation, which updates the display and
        // reports its own errors: those of this one are about old data.
        catch (Exception) when (cts.IsCancellationRequested)
        {
            return;
        }
        // Not our cancellation (filtered above): a timeout is "too slow".
        catch (Exception ex)
        {
            ReportRecommendationError(NoticeCenter.ServiceErrorMessage(
                ex, Strings.Error_GenresUnreachable, Strings.Error_CatalogSlow), ex);
        }

        // Only the latest computation may hide the progress bar.
        if (_recommendationsCts == cts)
            IsComputing = false;
    }

    /// <summary>
    /// Last computed taste and candidates: switching the filter only rebuilds
    /// the (quick) sections, without looking up genres again.
    /// </summary>
    private sealed record RecommendationState(
        IReadOnlyList<PlayedGameWithGenres> TasteGames,
        TasteProfile Profile,
        IReadOnlyList<Recommendation> Candidates,
        int ExcludedCount,
        int BarelyPlayedCount,
        // The sources with at least one candidate (the player's plans applied).
        IReadOnlySet<CatalogSource> SourcesWithCandidates,
        TagVectorizer Vectorizer);

    private RecommendationState? _recommendationState;

    // French names of the Steam tags, for "Même style : ..." on the cards.
    private IReadOnlyDictionary<int, string> _tagNames = new Dictionary<int, string>();

    // Cancels the picture loading of cards that are no longer shown.
    private CancellationTokenSource? _cardImagesCts;

    /// <summary>Rebuilt from the saved candidates: a few milliseconds, on the UI thread.</summary>
    public void RebuildSections()
    {
        if (_recommendationState is { } state)
            ShowRecommendations(state, BuildSections(state, _filter.Ticked, _saver.Settings.NotInterested.Keys.ToHashSet(), DateTimeOffset.UtcNow));
    }

    /// <summary>A box or a refusal changed: written in the background (an error shows on "Pour toi").</summary>
    public void SaveFilter() => _ = TrySaveAsync();

    /// <summary>Writes the settings; false, with the error shown on "Pour toi", when the disk refuses.</summary>
    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await _saver.SaveAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Localization.Format(Strings.Error_SaveChoice, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// The v2 sections, on the candidates of the ticked sources: in the
    /// player's Game Pass or PS Plus plan, sold on Steam, or several (a game in
    /// several shows as soon as one of the boxes is ticked). The refused games are looked
    /// up among every candidate, so a refused Steam game still keeps its
    /// look-alikes away while the Steam box is unticked.
    /// </summary>
    private static SimilarityResult BuildSections(
        RecommendationState state, IReadOnlyList<CatalogSource> ticked, IReadOnlySet<string> notInterested, DateTimeOffset now)
    {
        IReadOnlyCollection<CatalogGame> refused = notInterested.Count == 0 ? [] : state.Candidates
            .Where(c => notInterested.Contains(GameTitle.Key(c.Game.Title)))
            .Select(c => c.Game)
            .ToList();
        bool showGamePass = ticked.Contains(CatalogSource.GamePass);
        var candidates = state.Candidates
            .Where(c => ticked.Any(c.Game.IsIn))
            // Game Pass unticked: a game leaving it is only a Steam game here,
            // it must not open the "leaving the Game Pass" section.
            .Select(c => showGamePass || !c.Game.LeavingSoon ? c : c with { Game = c.Game with { LeavingSoon = false } })
            .ToList();
        return SimilarityRecommender.Build(state.TasteGames, candidates, now, tagVectorizer: state.Vectorizer, notInterested: refused);
    }

    /// <summary>The old cards go away: their pictures stop loading and are freed.</summary>
    private void ClearCards()
    {
        _cardImagesCts?.Cancel();
        foreach (var row in Sections.SelectMany(s => s.Games))
            row.ReleaseImage();
        Sections.Clear();
    }

    /// <summary>Replaces the cards on screen, then loads their pictures.</summary>
    private void ShowRecommendations(RecommendationState state, SimilarityResult result)
    {
        // The old cards go away: stop their downloads, free their pictures.
        ClearCards();
        var imagesCts = new CancellationTokenSource();
        _cardImagesCts = imagesCts;

        // First: the only picks with a deadline.
        if (result.LeavingSoon.Count > 0)
            Sections.Add(new RecommendationSectionRow(
                Strings.Section_LeavingSoon,
                result.LeavingSoon.Select(p => new RecommendationRow(p, _tagNames)).ToList(),
                isLeavingSoon: true));
        foreach (var section in result.Sections)
            Sections.Add(new RecommendationSectionRow(
                Localization.Format(Strings.Section_BecauseYouLiked, section.Favorite.Name),
                section.Games.Select(g => new RecommendationRow(g, _tagNames)).ToList()));
        if (result.ByGenres.Count > 0)
            Sections.Add(new RecommendationSectionRow(
                Strings.Section_ByGenres,
                result.ByGenres.Select(r => new RecommendationRow(r, state.Profile, _tagNames)).ToList()));

        HasSteamCandidates = state.SourcesWithCandidates.Contains(CatalogSource.Steam);
        HasGogCandidates = state.SourcesWithCandidates.Contains(CatalogSource.Gog);
        HasEpicCandidates = state.SourcesWithCandidates.Contains(CatalogSource.Epic);
        var ignored = new List<string>();
        if (state.ExcludedCount > 0)
            ignored.Add(Localization.Format(Strings.Reco_ExcludedByYou, state.ExcludedCount));
        if (state.BarelyPlayedCount > 0)
            ignored.Add(Localization.Format(Strings.Reco_BarelyPlayed, state.BarelyPlayedCount));
        // The sources actually shown: a box can be ticked but unavailable
        // (no subscription, Steam catalog not loaded yet).
        var sources = _filter.Ticked.Where(state.SourcesWithCandidates.Contains).Select(_filter.Label).ToList();
        if (sources.Count == 0)
        {
            Status = Strings.Reco_NoSource;
        }
        else
        {
            // Whole sentences, never assembled from pieces: word order differs between languages.
            string offered = sources.Count switch
            {
                6 => Localization.Format(Strings.Reco_SixSources, sources[0], sources[1], sources[2], sources[3], sources[4], sources[5]),
                5 => Localization.Format(Strings.Reco_FiveSources, sources[0], sources[1], sources[2], sources[3], sources[4]),
                4 => Localization.Format(Strings.Reco_FourSources, sources[0], sources[1], sources[2], sources[3]),
                3 => Localization.Format(Strings.Reco_ThreeSources, sources[0], sources[1], sources[2]),
                2 => Localization.Format(Strings.Reco_TwoSources, sources[0], sources[1]),
                _ => sources[0]
            };
            Status = ignored.Count == 0
                ? Localization.Format(Strings.Reco_Summary, result.Sections.Count, state.TasteGames.Count, offered)
                : Localization.Format(Strings.Reco_SummaryIgnored, result.Sections.Count, state.TasteGames.Count, string.Join(", ", ignored), offered);
        }

        // The cards are already on screen; pictures fill in as they arrive.
        _work.Track(LoadCardImagesAsync(Sections.SelectMany(s => s.Games).ToList(), imagesCts.Token));
    }

    /// <summary>
    /// Downloads (or reads from disk) then decodes each card's picture.
    /// Never throws: a picture that fails keeps the card's placeholder.
    /// </summary>
    /// <param name="ct">Cancelled when newer recommendations replace these cards.</param>
    private async Task LoadCardImagesAsync(IReadOnlyList<RecommendationRow> rows, CancellationToken ct)
    {
        // The downloads a few at a time (as many as the cache makes), in the
        // cards' order: when newer cards replace these (a filter click), those
        // not started yet are never made. A started one is not stopped:
        // ImageFileCache shares it with every card of the same game.
        using var downloads = new SemaphoreSlim(ImageFileCache.MaxParallelDownloads);
        await Task.WhenAll(rows.Where(r => r.ImageUrl is not null).Select(LoadAsync));

        async Task LoadAsync(RecommendationRow row)
        {
            try
            {
                // Already on disk: at once, never behind the downloads.
                string? path = _imageCache.CachedPath(row.ImageUrl!) ?? await DownloadAsync(row.ImageUrl!);
                if (path is null || ct.IsCancellationRequested)
                    return;

                // Decoding a JPEG is CPU work: off the UI thread. DecodeToWidth
                // keeps only the pixels the card needs in memory.
                var bitmap = await Task.Run(() =>
                {
                    ImageFileCache.MarkShown(path);
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, RecommendationRow.ImageDecodeWidth);
                });

                // Back on the UI thread. If the cards were replaced meanwhile,
                // nobody will show this picture: free it right away.
                if (ct.IsCancellationRequested)
                    bitmap.Dispose();
                else
                    row.Image = bitmap;
            }
            catch (Exception)
            {
                // Corrupted file, unsupported format...: keep the placeholder.
            }
        }

        async Task<string?> DownloadAsync(string url)
        {
            await downloads.WaitAsync(ct);
            try
            {
                return await _imageCache.GetAsync(url);
            }
            finally
            {
                downloads.Release();
            }
        }
    }

    // Kept until it ends: between the save and the recomputation, nothing else is running.
    public Task SaveChoicesAndRecomputeAsync() => _work.Track(SaveAndRecomputeAsync());

    private async Task SaveAndRecomputeAsync()
    {
        if (!await TrySaveAsync())
            return;

        // Genres are cached: this is only a quick recomputation.
        await UpdateAsync();
    }
}
