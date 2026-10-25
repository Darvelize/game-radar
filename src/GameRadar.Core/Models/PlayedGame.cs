namespace GameRadar.Core.Models;

/// <summary>
/// A game from the player's history, in a platform-agnostic shape.
/// Every connector (Xbox, Steam, maybe GOG later) must produce
/// this type, so the recommendation engine never depends on a platform.
/// </summary>
/// <param name="Platform">Source platform: "Xbox", "Steam", "PlayStation" or "Manual" (each connector's PlatformName).</param>
/// <param name="PlatformId">Identifier of the game on that platform (Xbox titleId, Steam appid, PlayStation title or trophy set id...).</param>
/// <param name="Name">Display name.</param>
/// <param name="HasAchievements">True when the game exposes achievements at all.</param>
/// <param name="UnlockedAchievements">Achievements earned by the player.</param>
/// <param name="TotalAchievements">
/// Achievements available in the game, or null when the platform does not
/// report it reliably (Xbox often sends 0 for games that have achievements).
/// </param>
/// <param name="ProgressPercentage">Completion from 0 to 100, the most reliable progress signal.</param>
/// <param name="LastPlayed">Last time the game was played, if known.</param>
/// <param name="Status">Derived play status.</param>
/// <param name="PackageFamilyName">
/// Microsoft Store package id, when available. Useful later to match
/// the game against the Game Pass catalog without comparing names.
/// </param>
/// <param name="PlaytimeMinutes">
/// Total play time, when the platform reports it reliably (Steam), else
/// null (Xbox). 0 = owned but never launched.
/// </param>
/// <param name="DeclaredByPlayer">
/// How far the player went is their own statement (a game added by hand,
/// <see cref="Manual.ManualGames"/>), set in <paramref name="Status"/> and
/// <paramref name="ProgressPercentage"/>: no achievements nor play time,
/// but a taste signal all the same, even "didn't like it".
/// </param>
public sealed record PlayedGame(
    string Platform,
    string PlatformId,
    string Name,
    bool HasAchievements,
    int UnlockedAchievements,
    int? TotalAchievements,
    double ProgressPercentage,
    DateTimeOffset? LastPlayed,
    PlayStatus Status,
    string? PackageFamilyName = null,
    int? PlaytimeMinutes = null,
    bool DeclaredByPlayer = false)
{
    /// <summary>
    /// Its Steam app id when its connector knows it (a Steam game, a game
    /// added by hand and found on Steam): its genres, tags and exclusion
    /// from the recommendations follow it, whatever the title.
    /// </summary>
    public string? SteamAppId { get; init; }

    /// <summary>
    /// Its picture, when its history gives one (Xbox: 16:9; GOG: at the card's size), as is:
    /// the card of one of the player's games in no catalog ("Mes jeux").
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// Owned but never launched (Steam library): says nothing about taste. No minutes
    /// but achievements is started (played offline, seconds rounded down).
    /// </summary>
    public bool NeverPlayed => PlaytimeMinutes == 0 && UnlockedAchievements == 0 && ProgressPercentage == 0;

    /// <summary>
    /// True when the game tells something about the player's taste:
    /// achievements (their progress), a known play time, or the player's own
    /// statement (<see cref="DeclaredByPlayer"/>).
    /// </summary>
    public bool HasEngagementSignal => HasAchievements || PlaytimeMinutes > 0 || DeclaredByPlayer;

    /// <summary>
    /// Stable identity across platforms, e.g. "Xbox:1902411725". Used to
    /// remember per-game user choices; two platforms may reuse the same id.
    /// </summary>
    public string Key => KeyOf(Platform, PlatformId);

    /// <summary>The <see cref="Key"/> of a game, before it is built (a connector's own lookups).</summary>
    public static string KeyOf(string platform, string platformId) => $"{platform}:{platformId}";
}
