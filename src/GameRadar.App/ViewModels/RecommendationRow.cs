using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One recommended game, with every label already formatted.
/// Observable because its picture arrives after the card is shown.
/// </summary>
public sealed partial class RecommendationRow : ObservableObject
{
    // Twice the card width (280 px), so the picture stays sharp on high-DPI
    // screens; the image server resizes on the fly (~30 KB instead of ~250 KB).
    private const string ImageSize = "w=560&h=315&q=80";
    private const string EpicImageSize = "resize=1&w=640&h=360&quality=medium";
    public const int ImageDecodeWidth = 560;
    /// <summary>A game close to a favorite: the reason is what they share.</summary>
    /// <param name="tagNames">French names of the Steam tags (tag id → name).</param>
    public RecommendationRow(SimilarGame similar, IReadOnlyDictionary<int, string>? tagNames = null)
        : this(similar.Recommendation.Game, tagNames)
    {
        ReasonLabel = Localization.Format(Strings.Reason_SameStyle, SharedStyle(similar, tagNames));
    }

    /// <summary>
    /// A game leaving soon: each one may resemble a different favorite,
    /// so the reason names it.
    /// </summary>
    public RecommendationRow(LeavingSoonPick pick, IReadOnlyDictionary<int, string>? tagNames = null)
        : this(pick.Game.Recommendation.Game, tagNames)
    {
        ReasonLabel = Localization.Format(Strings.Reason_CloseTo, pick.ClosestFavorite.Name, SharedStyle(pick.Game, tagNames));
    }

    /// <summary>
    /// The most telling Steam tags both games have ("Jeu de tir et de butin ·
    /// RPG d'action"), or, when compared on genres only, the shared genres and modes.
    /// </summary>
    private static string SharedStyle(SimilarGame similar, IReadOnlyDictionary<int, string>? tagNames)
    {
        var tags = (similar.SharedTags ?? [])
            .Select(id => tagNames?.GetValueOrDefault(id))
            .OfType<string>()
            .ToList();
        if (similar.ByTags && tags.Count > 0)
            return string.Join(" · ", tags);

        return string.Join(" · ", similar.SharedGenres.Select(GenreLabels.Display)
            .Concat(PlayModeLabels.Display(similar.SharedModes)));
    }

    /// <summary>A game picked for the Steam tags (or genres) the player goes far in.</summary>
    /// <param name="tagNames">Names of the Steam tags in the app's language (tag id → name).</param>
    public RecommendationRow(Recommendation recommendation, TasteProfile profile, IReadOnlyDictionary<int, string>? tagNames = null)
        : this(recommendation.Game, tagNames)
    {
        // Explains the score with the player's own history, e.g.
        // "Parce que tu vas loin dans : Monde ouvert (12 jeux), Scénario riche (9 jeux)".
        var reasons = (recommendation.ReasonTags ?? [])
            .Select(tag => (Name: tagNames?.GetValueOrDefault(tag), Games: profile.StrongGamesWithTag(tag)))
            .Concat(recommendation.ReasonGenres.Select(g => (Name: (string?)GenreLabels.Display(g), Games: profile.StrongGamesIn(g))))
            .Where(r => r.Name is not null)
            .Take(2)
            .Select(r => Localization.Format(Strings.Reason_GenreGames, r.Name, r.Games))
            .ToList();
        ReasonLabel = reasons.Count == 0
            ? Strings.Reason_ToDiscover
            : Localization.Format(Strings.Reason_GoFarIn, string.Join(", ", reasons));
    }

    // Labels common to both kinds of rows.
    private RecommendationRow(CatalogGame game, IReadOnlyDictionary<int, string>? tagNames)
    {
        Title = game.Title;
        TitleKey = GameTitle.Key(game.Title);
        // Its 3 most voted Steam tags as badges, much finer than the Store genres;
        // the genres in text for a game without tags (or before the tag names are known).
        StyleTags = SteamTagIndex.TopNames(game.Tags, tagNames).Select(name => Localization.Format(Strings.Tag_Badge, name)).ToList();
        StyleLabel = StyleTags.Count > 0 ? string.Empty : string.Join(" · ", game.Genres.Select(GenreLabels.Display));

        // The picture comes from the entry the merge kept, the rating from its RatingStore.
        var store = game.PrimaryStore;
        var ratingStore = game.RatingStore;

        // Split in two so the view can show a star icon, the value in bold
        // and the count in secondary text. A Steam or GOG game is rated as
        // on its store: its share of positive reviews.
        HasRating = game.AverageRating is not null;
        RatingValueLabel = game.AverageRating is not { } rating ? "–"
            : ratingStore is CatalogSource.Steam or CatalogSource.Gog ? Localization.Format(Strings.Rating_Percent, rating * 20)
            // Epic: an average of 5 stars, like the Store, not a share of positive reviews.
            : rating.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        RatingCountLabel = !HasRating ? Strings.Rating_None : ratingStore switch
        {
            CatalogSource.Steam => Localization.Format(Strings.Rating_SteamCount, game.RatingCount),
            CatalogSource.Gog => Localization.Format(Strings.Rating_GogCount, game.RatingCount),
            CatalogSource.Epic => Strings.Rating_EpicPlayers,
            CatalogSource.PlayStationStore => Strings.Rating_PsPlayers,
            _ => Localization.Format(Strings.Rating_Count, game.RatingCount)
        };

        // On PlayStation (PS Plus or the store) and a PC store: the console first. The PC
        // store is the first that sells it, as in the merge (a PS Plus game keeps its PS Plus entry).
        bool onPlayStation = game.IsIn(CatalogSource.PsPlus) || game.IsIn(CatalogSource.PlayStationStore);
        CatalogSource? pcStore = game.IsIn(CatalogSource.Steam) ? CatalogSource.Steam
            : game.IsIn(CatalogSource.Gog) ? CatalogSource.Gog
            : game.IsIn(CatalogSource.Epic) ? CatalogSource.Epic
            : null;
        string platforms = (game.OnPc, game.OnConsole) switch
        {
            (true, true) => Strings.Platform_PcAndConsole,
            (true, false) => Strings.Platform_Pc,
            (false, true) => Strings.Platform_Console,
            // Not in the Game Pass lists: PlayStation and the PC store.
            _ => (onPlayStation, pcStore) switch
            {
                (true, CatalogSource.Steam) => Strings.Platform_PlayStationAndSteam,
                (true, CatalogSource.Gog) => Strings.Platform_PlayStationAndGog,
                (true, CatalogSource.Epic) => Strings.Platform_PlayStationAndEpic,
                (true, _) => Strings.Platform_PlayStation,
                (false, CatalogSource.Gog) => Strings.Platform_PcGog,
                (false, CatalogSource.Epic) => Strings.Platform_PcEpic,
                _ => Strings.Platform_PcSteam
            }
        };
        var modes = PlayModeLabels.Display(game.Modes).ToList();
        PlatformsLabel = modes.Count == 0 ? platforms : $"{platforms} · {string.Join(", ", modes)}";

        IsLeavingSoon = game.LeavingSoon;
        // The game's page on each platform it shows a badge for: the player
        // chooses where to go (a game may be in the Game Pass and on Steam).
        XboxStorePage = game.InGamePass
            ? new Uri($"https://www.xbox.com/{Localization.XboxStoreLocale}/games/store/-/{Uri.EscapeDataString(game.ProductId)}")
            : null;
        PsPlusPage = game.InPsPlus && game.PlayStationConceptId is { } conceptId
            ? new Uri($"https://store.playstation.com/{Localization.PlayStationStoreLocale}/concept/{Uri.EscapeDataString(conceptId)}")
            : null;
        // A game of the player's PS Plus already opens its store page by its "PS Plus" badge.
        PsStorePage = !game.InPsPlus && game.PlayStationProductId is { } productId
            ? new Uri($"https://store.playstation.com/{Localization.PlayStationStoreLocale}/product/{Uri.EscapeDataString(productId)}")
            : null;
        // l=: the page in the language of the app, whatever the Steam account's.
        SteamPage = game.SteamAppId is { } appId
            ? new Uri($"https://store.steampowered.com/app/{Uri.EscapeDataString(appId)}/?l={Localization.SteamLanguage}")
            : null;
        // The link comes from GOG's answer, as is: only an https page of gog.com is opened.
        GogPage = Uri.TryCreate(game.GogStoreLink, UriKind.Absolute, out var gogPage)
            && gogPage.Scheme == Uri.UriSchemeHttps
            && (gogPage.Host == "gog.com" || gogPage.Host.EndsWith(".gog.com", StringComparison.OrdinalIgnoreCase))
            ? gogPage : null;
        // The same for Epic: an https page of store.epicgames.com only, opened in
        // the language of the app (the catalog keeps "/p/<slug>", without language).
        EpicPage = Uri.TryCreate(game.EpicStoreLink, UriKind.Absolute, out var epicPage)
            && epicPage.Scheme == Uri.UriSchemeHttps && epicPage.Host == "store.epicgames.com"
            ? new Uri($"https://store.epicgames.com/{Localization.EpicStoreLocale}{epicPage.AbsolutePath}")
            : null;
        ReasonLabel = string.Empty;
        // The Microsoft and PlayStation image servers resize on the fly (the
        // PlayStation pictures are 1024 px squares); Steam pictures come at
        // their size (616 × 353), GOG ones already at the card's (639 × 361); Epic's
        // CDN resizes with its own parameters (946 KB → 49 KB, measured).
        // A PS Plus game sold on Steam keeps its PlayStation picture.
        ImageUrl = game.ImageUrl is not { } url ? null : store switch
        {
            CatalogSource.Epic => WithQuery(url, EpicImageSize),
            CatalogSource.Steam or CatalogSource.Gog => url,
            _ => WithQuery(url, ImageSize)
        };
    }

    private static string WithQuery(string url, string query) => url + (url.Contains('?') ? "&" : "?") + query;

    /// <summary>Resized picture address, or null when the game has none.</summary>
    public string? ImageUrl { get; }

    /// <summary>Set once downloaded and decoded; until then the card shows its placeholder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; set; }

    public bool HasImage => Image is not null;

    /// <summary>
    /// Frees the decoded picture. A Bitmap holds native memory that the
    /// garbage collector does not see: dispose it when the card goes away.
    /// </summary>
    public void ReleaseImage()
    {
        var image = Image;
        Image = null;
        image?.Dispose();
    }

    public string Title { get; }

    /// <summary>The game whatever its store (<see cref="GameTitle.Key"/>): what "Pas intéressé" saves.</summary>
    public string TitleKey { get; }

    /// <summary>"#Tir avec butin", "#Multijoueur", "#Tir à la 3ᵉ personne": its 3 most voted Steam tags, as badges.</summary>
    public IReadOnlyList<string> StyleTags { get; }

    public bool HasStyleTags => StyleTags.Count > 0;

    /// <summary>"Tir · Action-aventure": its genres, only when it has no tags.</summary>
    public string StyleLabel { get; }
    public string ReasonLabel { get; }
    public bool HasRating { get; }
    public string RatingValueLabel { get; }
    public string RatingCountLabel { get; }
    public string PlatformsLabel { get; }
    public bool IsLeavingSoon { get; }
    /// <summary>Playable with the subscription: green "Game Pass" badge, which opens its page.</summary>
    public bool InGamePass => XboxStorePage is not null;
    /// <summary>In the player's PS Plus: "PS Plus" badge, which opens its page on the PlayStation store.</summary>
    public bool InPsPlus => PsPlusPage is not null;
    /// <summary>Sold on Steam (only there, or also in a subscription): "Steam" badge, which opens its page.</summary>
    public bool IsOnSteam => SteamPage is not null;
    /// <summary>Sold on GOG (only there, or also elsewhere): "GOG" badge, which opens its page.</summary>
    public bool IsOnGog => GogPage is not null;
    /// <summary>Sold on Epic (only there, or also elsewhere): "Epic" badge, which opens its page.</summary>
    public bool IsOnEpic => EpicPage is not null;
    /// <summary>Among the PlayStation Store best sellers, out of the player's PS Plus: "PlayStation" badge, which opens its page.</summary>
    public bool IsOnPsStore => PsStorePage is not null;

    /// <summary>Its page on the Xbox store, opened by its "Game Pass" badge; null out of the player's Game Pass.</summary>
    public Uri? XboxStorePage { get; }

    /// <summary>Its page on the PlayStation store, opened by its "PS Plus" badge; null out of the player's PS Plus.</summary>
    public Uri? PsPlusPage { get; }

    /// <summary>Its page on the PlayStation Store, opened by its "PlayStation" badge; null in the player's PS Plus or not sold there.</summary>
    public Uri? PsStorePage { get; }

    /// <summary>Its page on Steam, opened by its "Steam" badge; null when not sold there.</summary>
    public Uri? SteamPage { get; }

    /// <summary>Its page on GOG, opened by its "GOG" badge; null when not sold there.</summary>
    public Uri? GogPage { get; }

    /// <summary>Its page on Epic, opened by its "Epic" badge; null when not sold there.</summary>
    public Uri? EpicPage { get; }
}

/// <summary>A titled group of recommendations ("Parce que tu as aimé Destiny 2").</summary>
/// <param name="isLeavingSoon">The time-limited section, highlighted differently.</param>
public sealed class RecommendationSectionRow(string title, IReadOnlyList<RecommendationRow> games, bool isLeavingSoon = false)
{
    public string Title { get; } = title;
    public IReadOnlyList<RecommendationRow> Games { get; } = games;
    public bool IsLeavingSoon { get; } = isLeavingSoon;
}
