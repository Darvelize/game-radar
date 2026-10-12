using System.Text.Json.Serialization;
using GameRadar.Core.Services;

namespace GameRadar.Core.Manual;

/// <summary>
/// A game the player added by hand: played on a platform GameRadar cannot
/// read (PlayStation, Switch, another PC store, an old console), or without
/// syncing any account. Saved in the settings.
/// </summary>
/// <param name="Title">The title as found (Game Pass catalog, Steam store) or as typed.</param>
/// <param name="SteamAppId">Steam app id when found on the Steam store: finds its genres there.</param>
/// <param name="Genres">Its genres when the catalog it was found in lists them (the PlayStation Store, PS Plus), in the common vocabulary: the last resort when no other source knows the game.</param>
public sealed record ManualGame(
    string Title,
    ManualPlatform Platform,
    ManualLevel Level,
    string? SteamAppId = null,
    IReadOnlyList<string>? Genres = null)
{
    /// <summary>
    /// Identity of the game, stable while it is edited: its Steam app id,
    /// else its title key (<see cref="GameTitle.Key"/>). Adding the same game
    /// twice replaces it. A title without letters or digits ("???") has an
    /// empty key: the title itself then, or all of them would be one game.
    /// </summary>
    [JsonIgnore]
    public string Id => SteamAppId
        ?? ManualGames.TitleIdPrefix + (GameTitle.Key(Title) is { Length: > 0 } key ? key : Title.Trim());
}

/// <summary>
/// Where the game was played (a fixed list: consistent badges, translated).
/// Families, not consoles: the engine ignores the platform. The values are
/// saved by name in settings.json: never rename one.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ManualPlatform>))]
public enum ManualPlatform
{
    PlayStation,
    /// <summary>Nintendo, whatever the console (shown "Nintendo"; saved as "Switch" since 1.2.0).</summary>
    Switch,
    /// <summary>A PC store GameRadar cannot read: Epic, GOG, Ubisoft Connect, EA app…</summary>
    OtherPcStore,
    /// <summary>Xbox, whatever the console (shown "Xbox"; saved as "OldXbox" since 1.2.0, when only the older ones were missing).</summary>
    OldXbox,
    Other,
    Android,
    Ios
}

/// <summary>
/// How far the player went, said by them (no achievements nor play time to
/// measure it). Same weights as a synced game (<see cref="TasteProfile.Engagement"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ManualLevel>))]
public enum ManualLevel
{
    /// <summary>100 %: platinum trophy, every achievement. Weight 3.</summary>
    Mastered,
    /// <summary>Story finished. Weight 3.</summary>
    Completed,
    /// <summary>Played a lot, like ≥ 50 % of the achievements. Weight 2.</summary>
    PlayedALot,
    /// <summary>Played a little, like ≥ 5 %. Weight 0.5.</summary>
    PlayedALittle,
    /// <summary>Didn't like it: a negative signal (-0.5), counted by default.</summary>
    DidNotLike
}
