using System.Text.Json.Serialization;
using GameRadar.Core.Models;

namespace GameRadar.Core.Settings;

/// <summary>User settings persisted between sessions.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Keys (<see cref="Models.PlayedGame.Key"/>) of games the user does not
    /// want in their taste profile, e.g. completed only for gamerscore.
    /// </summary>
    public HashSet<string> ExcludedFromTaste { get; set; } = [];

    /// <summary>
    /// Keys of games the user ticked "Compte dans mes goûts" although the
    /// automatic rule leaves them out (barely played, see <see cref="Services.TasteSelection"/>).
    /// A choice equal to the rule is stored nowhere: the rule decides.
    /// </summary>
    public HashSet<string> IncludedInTaste { get; set; } = [];

    /// <summary>
    /// Keys of games whose story the user finished. Achievements cannot tell
    /// it, so the user marks them by hand (<see cref="Models.PlayStatus.Completed"/>).
    /// </summary>
    public HashSet<string> MarkedCompleted { get; set; } = [];

    /// <summary>
    /// Games the player said "Pas intéressé" to on a recommendation card:
    /// title key (<see cref="Services.GameTitle.Key"/>, the same game on the
    /// Game Pass and on Steam) → title as shown, for the list in the settings.
    /// Never suggested again, nor the games that look too much like them
    /// (<see cref="Services.SimilarityRecommender"/>).
    /// </summary>
    public Dictionary<string, string> NotInterested { get; set; } = [];

    /// <summary>
    /// Games the player added by hand (PlayStation, Switch, another PC store,
    /// an old console...): a history of their own, next to Xbox and Steam
    /// (<see cref="Manual.ManualGames"/>).
    /// </summary>
    public List<Manual.ManualGame> ManualGames { get; set; } = [];

    /// <summary>
    /// Steam tags the player gave their games, in the order added: title key
    /// (<see cref="Services.GameTitle.Key"/>, the game on every platform) → tag
    /// ids (never names: the computation compares ids, names follow the language).
    /// Count as much as a game's most voted Steam tag (<see cref="Steam.SteamTagIndex"/>).
    /// </summary>
    public Dictionary<string, List<int>> AddedTags { get; set; } = [];

    /// <summary>
    /// The player's Game Pass plan: "Game Pass" games are those of this plan
    /// only (higher plans are hidden). Ultimate by default: every game.
    /// Saved as text ("Premium"), readable in settings.json.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<GamePassTier>))]
    public GamePassTier GamePassPlan { get; set; } = GamePassTier.Ultimate;

    /// <summary>"Pour toi" filter: show the games of the player's Game Pass plan.</summary>
    public bool ShowGamePass { get; set; } = true;

    /// <summary>
    /// The player's PS Plus plan: "PS Plus" games are those of this plan only
    /// (Premium games are hidden from an Extra subscriber). None by default:
    /// most players have no PS Plus, and nothing says which plan they have.
    /// Saved as text ("Extra"), readable in settings.json.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PsPlusTier>))]
    public PsPlusTier PsPlusPlan { get; set; } = PsPlusTier.None;

    /// <summary>"Pour toi" filter: show the games of the player's PS Plus plan.</summary>
    public bool ShowPsPlus { get; set; } = true;

    /// <summary>"Pour toi" filter: show the games sold on Steam.</summary>
    public bool ShowSteam { get; set; } = true;

    /// <summary>"Pour toi" filter: show the games sold on GOG.</summary>
    public bool ShowGog { get; set; } = true;

    /// <summary>"Pour toi" filter: show the games sold on Epic.</summary>
    public bool ShowEpic { get; set; } = true;

    /// <summary>
    /// "Pour toi" filter: show the games sold on the PlayStation Store. Null
    /// until the player ticks or unticks the box: the box then follows the rule
    /// (ticked with a PlayStation history). Not written when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowPlayStationStore { get; set; }

    /// <summary>
    /// Name of the player's public GOG profile, or null when not connected.
    /// Public, not a secret: kept in the settings. Not written when null,
    /// so the file of a player without GOG keeps its format.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GogUser { get; set; }

    /// <summary>
    /// Last version whose news the user was shown ("1.2.0"), to announce the
    /// news once after an update. Null: never recorded (first launch, or an
    /// update from 1.1.0 or older, which did not record it).
    /// </summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Language of the interface: "fr", "en", or null for the Windows language.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Independent copy, e.g. to save a snapshot while the live settings keep
    /// changing. Kept next to the properties: a new property must be added here.
    /// </summary>
    public AppSettings Clone() => new()
    {
        ExcludedFromTaste = new HashSet<string>(ExcludedFromTaste),
        IncludedInTaste = new HashSet<string>(IncludedInTaste),
        MarkedCompleted = new HashSet<string>(MarkedCompleted),
        NotInterested = new Dictionary<string, string>(NotInterested),
        // Records: copying the list is enough, a game is replaced, never modified.
        ManualGames = [.. ManualGames],
        AddedTags = AddedTags.ToDictionary(e => e.Key, e => new List<int>(e.Value)),
        GamePassPlan = GamePassPlan,
        ShowGamePass = ShowGamePass,
        PsPlusPlan = PsPlusPlan,
        ShowPsPlus = ShowPsPlus,
        ShowSteam = ShowSteam,
        ShowGog = ShowGog,
        ShowEpic = ShowEpic,
        ShowPlayStationStore = ShowPlayStationStore,
        GogUser = GogUser,
        LastSeenVersion = LastSeenVersion,
        Language = Language
    };
}
