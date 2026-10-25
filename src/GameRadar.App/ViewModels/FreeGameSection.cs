using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One source of the "Jeux gratuits" page (Epic, Steam, PS Plus, Amazon): its cached list,
/// how it is downloaded, and its cards, split between the page's two tabs (games to keep,
/// games free for a while). Its status line says it is fetching or why
/// the last download failed (<see cref="FetchLine"/>); the list it had stays shown.
/// </summary>
public sealed partial class FreeGameSection : FetchLine
{
    // A source that failed (down, or its site changed) is not asked again at each visit.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    // The cards shown, by game: kept from one visit (or download) to the next while
    // the game is still offered, with their picture.
    private Dictionary<FreeGame, FreeGameRow> _rows = [];
    private DateTimeOffset? _failedAt;

    public FreeGameSection()
    {
        Kept = new(this);
        Timed = new(this);
    }

    /// <summary>The brand, never translated: "Epic Games".</summary>
    public required string Title { get; init; }

    public required FreeGameCache Cache { get; init; }

    /// <summary>Downloads the games free now (the connector's call), given the list it had.</summary>
    public required Func<DateTimeOffset, FreeGameList?, Task<IReadOnlyList<FreeGame>>> Fetch { get; init; }

    /// <summary>A card from a game, or null when its link is not a page of the expected site.</summary>
    public required Func<FreeGame, FreeGameRow?> ToRow { get; init; }

    /// <summary>Asked only when wanted (PS Plus, Xbox: for a player who has a plan); the hint says how otherwise.</summary>
    public required Func<bool> IsWanted { get; init; }

    /// <summary>"Choisis ton abonnement PS Plus…": shown instead of the cards when not wanted.</summary>
    public string? Hint { get; init; }

    /// <summary>Null until read from the cache or downloaded.</summary>
    public FreeGameList? List { get; set; }

    /// <summary>All its cards, both tabs (their pictures).</summary>
    public IReadOnlyCollection<FreeGameRow> Games => _rows.Values;

    /// <summary>Its games to keep ("Définitif" tab).</summary>
    public FreeGameCards Kept { get; }

    /// <summary>Its games free for a while, <see cref="FreeGame.PlayOnly"/> ("Temporaire" tab).</summary>
    public FreeGameCards Timed { get; }

    /// <summary>"Choisis ton abonnement PS Plus…": the source is not asked.</summary>
    [ObservableProperty]
    public partial bool ShowsHint { get; private set; }

    /// <summary>Wanted, its list stale, and no failure in the last minutes.</summary>
    public bool NeedsDownload(DateTimeOffset now) =>
        IsWanted() && FreeGameCache.NeedsDownload(List, now) && !(now - _failedAt < RetryAfterFailure);

    /// <summary>Its cards from its list: the offers still running only. A card no longer shown frees its picture.</summary>
    /// <param name="inHistory">The title keys of the player's history (GameTitle.Key): "Dans ton historique" on those cards.</param>
    public void Show(DateTimeOffset now, IReadOnlySet<string> inHistory)
    {
        ShowsHint = !IsWanted();
        var rows = new Dictionary<FreeGame, FreeGameRow>();
        if (!ShowsHint)
        {
            foreach (var game in FreeGameCache.Current(List, now))
            {
                if ((_rows.GetValueOrDefault(game) ?? ToRow(game)) is { } row)
                {
                    row.InHistory = inHistory.Contains(GameTitle.Key(game.Title));
                    rows[game] = row;
                }
            }
        }
        foreach (var (game, row) in _rows)
        {
            if (!rows.ContainsKey(game))
                row.ReleaseImage();
        }
        // The same cards: the view keeps them as they are (no card rebuilt at each visit).
        if (!rows.Keys.SequenceEqual(_rows.Keys))
        {
            Kept.Games = rows.Where(r => !r.Key.PlayOnly).Select(r => r.Value).ToList();
            Timed.Games = rows.Where(r => r.Key.PlayOnly).Select(r => r.Value).ToList();
        }
        _rows = rows;
        Kept.ShowsNone = !ShowsHint && Kept.Games.Count == 0;
        Timed.ShowsNone = !ShowsHint && Timed.Games.Count == 0;
        // No plan any more: a failure of the plan's list says nothing now, the hint alone.
        if (ShowsHint)
            Error = null;
        ShowStatus(string.Empty);
    }

    /// <summary>Downloads its list and saves it; on failure the list it had is kept (and said on the page, not in the banner).</summary>
    public async Task<FetchResult> FetchAsync(NoticeCenter notices, DateTimeOffset now)
    {
        var result = await RunFetchAsync(async () => List = await Cache.SaveAsync(await Fetch(now, List)), notices, banner: false);
        _failedAt = result == FetchResult.Failed ? now : null;
        return result;
    }
}

/// <summary>A source's cards in one tab of the "Jeux gratuits" page: one block template for both tabs.</summary>
public sealed partial class FreeGameCards(FreeGameSection section) : ObservableObject
{
    /// <summary>Its title, status line and hint.</summary>
    public FreeGameSection Section { get; } = section;

    [ObservableProperty]
    public partial IReadOnlyList<FreeGameRow> Games { get; set; } = [];

    /// <summary>"Aucun jeu gratuit en ce moment."</summary>
    [ObservableProperty]
    public partial bool ShowsNone { get; set; }
}

/// <summary>A card of the "Jeux gratuits" page: everything already written for the view.</summary>
public sealed partial class FreeGameRow : PictureCard
{
    public FreeGameRow(string title, string offerLabel, string? redeemLabel, Uri page, string? imageUrl)
    {
        Title = title;
        OfferLabel = offerLabel;
        RedeemLabel = redeemLabel;
        Page = page;
        ImageUrl = imageUrl;
    }

    public string Title { get; }

    /// <summary>"Gratuit jusqu'au jeudi 22 octobre à 17:00", "Jeu du mois avec PlayStation Plus".</summary>
    public string OfferLabel { get; }

    /// <summary>"À récupérer sur GOG", or null.</summary>
    public string? RedeemLabel { get; }

    /// <summary>Where to get it, opened by its button.</summary>
    public Uri Page { get; }

    /// <summary>"Dans ton historique": the player has played it, or added it, already (same title).</summary>
    [ObservableProperty]
    public partial bool InHistory { get; set; }
}
