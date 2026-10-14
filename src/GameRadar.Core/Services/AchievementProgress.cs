using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// A game's achievements as the history shows them, from the counts a
/// platform gives (Steam, EA, Epic): one rule for "has none", the percentage
/// and the status.
/// </summary>
public readonly record struct AchievementProgress(int Unlocked, int? Total, double Percentage)
{
    public bool HasAchievements => Total is not null;

    public PlayStatus Status => StatusClassifier.FromAchievements(Unlocked, Total, Percentage);

    /// <param name="total">0: the game has none (whatever <paramref name="unlocked"/> says).</param>
    public static AchievementProgress From(int unlocked, int total) =>
        total > 0 ? new(unlocked, total, 100.0 * unlocked / total) : new(0, null, 0);
}

/// <summary>
/// The achievements of one game at a given "last played" date (Steam, EA):
/// asked again only once the game is played since. Total 0 = the game has none.
/// </summary>
internal sealed record AchievementEntry(int Unlocked, int Total, DateTimeOffset? LastPlayed);
