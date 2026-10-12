using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Tests.Services;

public class StatusClassifierTests
{
    [Theory]
    // The percentage alone is enough, even when the total is unknown (Xbox often sends 0).
    [InlineData(10, null, 100.0, PlayStatus.Mastered)]
    // The count is a fallback when the total is known.
    [InlineData(50, 50, 99.0, PlayStatus.Mastered)]
    [InlineData(49, 50, 98.0, PlayStatus.Played)]
    [InlineData(33, null, 61.0, PlayStatus.Played)]
    [InlineData(0, null, 0.0, PlayStatus.Played)]
    public void FromAchievements_ClassifiesByPercentageThenCount(int unlocked, int? total, double percentage, PlayStatus expected)
    {
        Assert.Equal(expected, StatusClassifier.FromAchievements(unlocked, total, percentage));
    }
}
