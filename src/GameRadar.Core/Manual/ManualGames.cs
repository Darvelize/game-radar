using GameRadar.Core.Models;

namespace GameRadar.Core.Manual;

/// <summary>
/// The "Manual" connector: turns the games added by hand into
/// <see cref="PlayedGame"/>, like the Xbox and Steam connectors, so the
/// recommendation engine does not know where they come from.
/// </summary>
public static class ManualGames
{
    public const string PlatformName = "Manual";

    /// <summary>Id of a game found nowhere (no Steam app id): "title:" + its title key.</summary>
    public const string TitleIdPrefix = "title:";

    /// <summary>
    /// The genres the games added by hand carry (found in the PlayStation Store or
    /// the PS Plus catalog), by the key of their played game: the same shape as the
    /// platform genres of a synced history, for a game no other source knows.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PlatformGenres(IEnumerable<ManualGame> games) =>
        games
            .Where(g => g.Genres is { Count: > 0 })
            .ToDictionary(g => ToPlayed(g).Key, g => g.Genres!);

    /// <summary>
    /// The level becomes what a synced game would have: its status and
    /// progress, weighed by the same rule (<see cref="Services.TasteProfile.Engagement"/>).
    /// No last-played date: a "didn't like it" is an old game, -0.5 at once.
    /// </summary>
    public static PlayedGame ToPlayed(ManualGame game)
    {
        var (status, progress) = game.Level switch
        {
            ManualLevel.Mastered => (PlayStatus.Mastered, 100.0),
            ManualLevel.Completed => (PlayStatus.Completed, 50.0),
            ManualLevel.PlayedALot => (PlayStatus.Played, 50.0),
            ManualLevel.PlayedALittle => (PlayStatus.Played, 5.0),
            _ => (PlayStatus.Played, 0.0)
        };
        return new PlayedGame(
            Platform: PlatformName,
            PlatformId: game.Id,
            Name: game.Title,
            HasAchievements: false,
            UnlockedAchievements: 0,
            TotalAchievements: null,
            ProgressPercentage: progress,
            LastPlayed: null,
            Status: status,
            DeclaredByPlayer: true)
        {
            // Found on Steam: its genres, tags and exclusion follow its app id.
            SteamAppId = game.SteamAppId
        };
    }
}
