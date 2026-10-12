using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// Puts the histories of several platforms together. Games are compared by
/// normalized name: platforms share no common id.
/// </summary>
public static class HistoryMerger
{
    /// <summary>
    /// All the games of the platforms, most recent first. The same game on
    /// several stays once per platform here (one line each, put together by
    /// <see cref="GroupByGame"/>); the games the Xbox PC app saw launched from
    /// Steam are already left out by the Xbox connector.
    /// </summary>
    public static IReadOnlyList<PlayedGame> Combine(params IReadOnlyList<PlayedGame>[] histories) =>
        histories.SelectMany(h => h).OrderByDescending(g => g.LastPlayed).ToList();

    /// <summary>
    /// The same game on several platforms (e.g. Warframe on Xbox and Steam)
    /// put together, one group per game. Groups keep the order of their most
    /// recent line; within a group, the most recently played comes first.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PlayedGame>> GroupByGame(IEnumerable<PlayedGame> games) =>
        games
            // GroupBy keeps the order in which each key first appears.
            .OrderByDescending(g => g.LastPlayed)
            .GroupBy(g => GameTitle.Key(g.Name))
            .Select(group => (IReadOnlyList<PlayedGame>)group.ToList())
            .ToList();

    /// <summary>
    /// One line per game for the taste: a game played on Xbox AND on Steam
    /// counts once, at the level the player went furthest. Counting it twice
    /// would double its weight in its genres. What the kept line does not
    /// know (its tags, its genres) is taken from another line of the same
    /// game: the Steam line knows the tags by app id, the Xbox one may not.
    /// </summary>
    public static IReadOnlyList<PlayedGameWithGenres> KeepBestPerGame(
        IEnumerable<PlayedGameWithGenres> games, DateTimeOffset now) =>
        games
            .GroupBy(g => GameTitle.Key(g.Game.Name))
            .Select(group =>
            {
                var best = group.MaxBy(g => TasteProfile.Engagement(g.Game, now))!;
                var withGenres = best.Genres.Count > 0 ? best : group.FirstOrDefault(g => g.Genres.Count > 0);
                return best with
                {
                    Genres = withGenres?.Genres ?? best.Genres,
                    Modes = withGenres?.Modes ?? best.Modes,
                    Tags = best.Tags ?? group.FirstOrDefault(g => g.Tags is not null)?.Tags
                };
            })
            .ToList();
}
