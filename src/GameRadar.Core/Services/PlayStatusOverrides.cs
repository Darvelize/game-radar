using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// Applies the statuses the user set by hand on top of the ones detected
/// from achievements.
/// </summary>
public static class PlayStatusOverrides
{
    /// <summary>
    /// Games marked as completed become <see cref="PlayStatus.Completed"/>,
    /// unless their detected status is already higher (Mastered): a manual
    /// mark can only raise a status, never lower it.
    /// </summary>
    public static IReadOnlyList<PlayedGame> Apply(IEnumerable<PlayedGame> games, IReadOnlySet<string> completedKeys) =>
        games
            .Select(g => completedKeys.Contains(g.Key) && g.Status < PlayStatus.Completed
                // "with" copies the immutable record, changing only Status.
                ? g with { Status = PlayStatus.Completed }
                : g)
            .ToList();
}
