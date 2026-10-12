using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// Turns raw achievement numbers into a <see cref="PlayStatus"/>.
/// Kept in its own class so the rule is easy to find, test and change.
/// </summary>
public static class StatusClassifier
{
    /// <param name="total">Total achievements, or null when unknown.</param>
    public static PlayStatus FromAchievements(int unlocked, int? total, double progressPercentage)
    {
        // The percentage is the primary signal: it stays correct even when
        // the platform does not report the total count.
        // The count is only a fallback, when the total is actually known.
        bool isComplete = progressPercentage >= 100
            || (total > 0 && unlocked >= total);

        return isComplete ? PlayStatus.Mastered : PlayStatus.Played;
    }
}
