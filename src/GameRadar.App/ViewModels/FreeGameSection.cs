using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One source of the "Jeux gratuits" page (Epic, Steam, PS Plus, Amazon): its cached list,
/// how it is downloaded, and its cards. Its status line says it is fetching or why
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

    /// <summary>The brand, never translated: "Epic Games".</summary>
    public required string Title { get; init; }

    public required FreeGameCache Cache { get; init; }

    /// <summary>Downloads the games free now (the connector's call).</summary>
    public required Func<DateTimeOffset, Task<IReadOnlyList<FreeGame>>> Fetch { get; init; }

    /// <summary>A card from a game, or null when its link is not a page of the expected site.</summary>
    public required Func<FreeGame, FreeGameRow?> ToRow { get; init; }

    /// <summary>Asked only when wanted (PS Plus: for a player who has a plan); the hint says how otherwise.</summary>
    public required Func<bool> IsWanted { get; init; }

    /// <summary>Null until read from the cache or downloaded.</summary>
    public FreeGameList? List { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNone))]
    public partial IReadOnlyList<FreeGameRow> Games { get; private set; } = [];

    /// <summary>"Choisis ton abonnement PS Plus…": the source is not asked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNone))]
    public partial bool ShowsHint { get; private set; }

    /// <summary>"Aucun jeu gratuit en ce moment."</summary>
    public bool ShowsNone => !ShowsHint && Games.Count == 0;

    /// <summary>Wanted, its list stale, and no failure in the last minutes.</summary>
    public bool NeedsDownload(DateTimeOffset now) =>
        IsWanted() && FreeGameCache.NeedsDownload(List, now) && !(now - _failedAt < RetryAfterFailure);

    /// <summary>Its cards from its list: the offers still running only. A card no longer shown frees its picture.</summary>
    public void Show(DateTimeOffset now)
    {
        ShowsHint = !IsWanted();
        var rows = new Dictionary<FreeGame, FreeGameRow>();
        if (!ShowsHint)
        {
            foreach (var game in FreeGameCache.Current(List, now))
            {
                if ((_rows.GetValueOrDefault(game) ?? ToRow(game)) is { } row)
                    rows[game] = row;
            }
        }
        foreach (var (game, row) in _rows)
        {
            if (!rows.ContainsKey(game))
                row.ReleaseImage();
        }
        // The same cards: the view keeps them as they are (no card rebuilt at each visit).
        if (!rows.Keys.SequenceEqual(_rows.Keys))
            Games = rows.Values.ToList();
        _rows = rows;
        ShowStatus(string.Empty);
    }

    /// <summary>Downloads its list and saves it; on failure the list it had is kept (and said on the page, not in the banner).</summary>
    public async Task<FetchResult> FetchAsync(NoticeCenter notices, DateTimeOffset now)
    {
        var result = await RunFetchAsync(async () => List = await Cache.SaveAsync(await Fetch(now)), notices, banner: false);
        _failedAt = result == FetchResult.Failed ? now : null;
        return result;
    }
}

/// <summary>A card of the "Jeux gratuits" page: everything already written for the view.</summary>
public sealed class FreeGameRow : PictureCard
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
}
