using System.Text.Json.Serialization;

namespace GameRadar.Core.Models;

/// <summary>
/// A game that can be recommended: from the Game Pass catalog, the PS Plus
/// catalog, the Steam, GOG or Epic store, or several (<see cref="InGamePass"/>,
/// <see cref="InPsPlus"/>, <see cref="SteamAppId"/>, <see cref="GogStoreLink"/>, <see cref="EpicStoreLink"/>).
/// </summary>
/// <param name="ProductId">
/// Unique key among candidates: the Microsoft Store product id (e.g.
/// "9NPDN9R45JX4"), "steam:&lt;appid&gt;" for a game only on Steam, or
/// "gog:&lt;id&gt;" for a game only on GOG.
/// </param>
/// <param name="Title">Display title, in the requested language.</param>
/// <param name="XboxTitleId">Xbox Live title id, when known: matches <see cref="PlayedGame.PlatformId"/> for Xbox games.</param>
/// <param name="PackageFamilyName">Store package id, when known.</param>
/// <param name="Genres">Genres, main one first (English labels from the Microsoft Store, the common vocabulary).</param>
/// <param name="OnPc">Playable on PC with the Game Pass (any plan, or the player's own after <see cref="ForPlan"/>).</param>
/// <param name="OnConsole">Playable on console with the Game Pass (same).</param>
/// <param name="LeavingSoon">Announced as leaving the Game Pass soon.</param>
/// <param name="AverageRating">Community rating out of 5, if any (Steam: share of positive reviews × 5).</param>
/// <param name="RatingCount">Number of community ratings behind <paramref name="AverageRating"/>.</param>
/// <param name="Modes">How the game can be played (solo, online co-op...).</param>
/// <param name="ImageUrl">Full-size card picture (16:9 when available); the display picks the size.</param>
/// <param name="SteamAppId">Steam app id, when the game is sold on Steam.</param>
/// <param name="Tags">
/// Steam user tags (tag id → vote weight relative to the most voted, 0 to 1),
/// when the game was found on Steam: what the similarity search compares.
/// </param>
/// <param name="ConsoleTiers">Game Pass plans that include the game on console.</param>
/// <param name="PcTiers">Game Pass plans that include the game on PC.</param>
/// <param name="PsPlusTiers">PlayStation Plus plans that include the game (Extra ⊂ Premium); none for a game outside PS Plus.</param>
/// <param name="PlayStationConceptId">PlayStation Store id of the game (its page), set with <paramref name="PsPlusTiers"/>.</param>
/// <param name="GogStoreLink">Its page on the GOG store, when GOG sells it.</param>
/// <param name="EpicStoreLink">Its page on the Epic store, when Epic sells it.</param>
/// <param name="PlayStationProductId">Its product on the PlayStation Store (its page), when the store's best sellers have it.</param>
public sealed record CatalogGame(
    string ProductId,
    string Title,
    string? XboxTitleId,
    string? PackageFamilyName,
    IReadOnlyList<string> Genres,
    bool OnPc,
    bool OnConsole,
    bool LeavingSoon,
    double? AverageRating = null,
    int RatingCount = 0,
    PlayModes Modes = PlayModes.None,
    string? ImageUrl = null,
    string? SteamAppId = null,
    IReadOnlyDictionary<int, double>? Tags = null,
    GamePassTier ConsoleTiers = GamePassTier.None,
    GamePassTier PcTiers = GamePassTier.None,
    PsPlusTier PsPlusTiers = PsPlusTier.None,
    string? PlayStationConceptId = null,
    // Not written when null: a catalog without GOG keeps the format of 1.4.0 and before.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GogStoreLink = null,
    // Not written when null either: the caches of 1.5.x read the same.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EpicStoreLink = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlayStationProductId = null)
{
    /// <summary>
    /// The game as a subscriber of <paramref name="plan"/> sees it: in the
    /// Game Pass only on the platforms where the plan includes it (a PC Game
    /// Pass subscriber has no console games, an Essential one far fewer
    /// games). Out of the plan: no longer in the Game Pass at all.
    /// A game with no known plan (catalog read without the plan lists)
    /// counts as in every plan.
    /// </summary>
    public CatalogGame ForPlan(GamePassTier plan)
    {
        if (plan == GamePassTier.None)
            return this with { OnPc = false, OnConsole = false, LeavingSoon = false };
        if (ConsoleTiers == GamePassTier.None && PcTiers == GamePassTier.None)
            return this;

        bool onConsole = (ConsoleTiers & plan) != 0;
        bool onPc = (PcTiers & plan) != 0;
        return this with { OnConsole = onConsole, OnPc = onPc, LeavingSoon = LeavingSoon && (onConsole || onPc) };
    }

    /// <summary>
    /// The game as a subscriber of <paramref name="plan"/> sees it: in PS Plus
    /// only when the plan includes it (an Extra subscriber has no Classics).
    /// Unlike <see cref="ForPlan"/>, no tier means "not in PS Plus", not "in every plan".
    /// </summary>
    public CatalogGame ForPsPlusPlan(PsPlusTier plan) => this with { PsPlusTiers = PsPlusTiers & plan };

    /// <summary>In PS Plus, for the plan this entry was filtered with (<see cref="ForPsPlusPlan"/>).</summary>
    [JsonIgnore]
    public bool InPsPlus => PsPlusTiers != PsPlusTier.None;

    /// <summary>
    /// Playable with a Game Pass subscription. Derived, not stored: a game
    /// from the Steam store is in none of the Game Pass lists.
    /// </summary>
    [JsonIgnore]
    public bool InGamePass => OnPc || OnConsole || LeavingSoon;

    /// <summary>
    /// The source whose entry the merge kept (CatalogMerger.Combine: the first of the
    /// Game Pass, PS Plus, Steam, GOG, Epic and the PlayStation Store that has the game):
    /// where its picture comes from, and its rating but for <see cref="RatingStore"/>.
    /// </summary>
    [JsonIgnore]
    public CatalogSource PrimaryStore => MergeOrder.FirstOrDefault(IsIn, CatalogSource.PlayStationStore);

    // The enum is declared in the order of the merge.
    private static readonly CatalogSource[] MergeOrder = Enum.GetValues<CatalogSource>();

    /// <summary>
    /// Where its rating comes from: its <see cref="PrimaryStore"/>, except a PS Plus game
    /// sold on Steam, which has Steam's rating (PS Plus has none: CatalogMerger copies it).
    /// </summary>
    [JsonIgnore]
    public CatalogSource RatingStore => PrimaryStore == CatalogSource.PsPlus && IsIn(CatalogSource.Steam)
        ? CatalogSource.Steam
        : PrimaryStore;

    /// <summary>Offered by <paramref name="source"/>: a game may be in several (one badge each).</summary>
    public bool IsIn(CatalogSource source) => source switch
    {
        CatalogSource.GamePass => InGamePass,
        CatalogSource.PsPlus => InPsPlus,
        CatalogSource.Steam => SteamAppId is not null,
        CatalogSource.Gog => GogStoreLink is not null,
        CatalogSource.Epic => EpicStoreLink is not null,
        CatalogSource.PlayStationStore => PlayStationProductId is not null,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}

/// <summary>Where a candidate can come from, in the order of the merge (CatalogMerger.Combine).</summary>
public enum CatalogSource { GamePass, PsPlus, Steam, Gog, Epic, PlayStationStore }

/// <summary>A whole catalog (Game Pass or Steam store) and when it was fetched.</summary>
public sealed record GameCatalog(IReadOnlyList<CatalogGame> Games, DateTimeOffset FetchedAt);
