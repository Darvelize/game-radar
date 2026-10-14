using System.Text.Json;
using GameRadar.Core.EA;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Tests.Services;

public class AchievementProgressTests
{
    [Fact]
    public void From_AGameWithoutAchievements_HasNone()
    {
        // Total 0: the game has none, whatever "unlocked" says.
        var progress = AchievementProgress.From(unlocked: 3, total: 0);

        Assert.False(progress.HasAchievements);
        Assert.Equal(0, progress.Unlocked);
        Assert.Null(progress.Total);
        Assert.Equal(0, progress.Percentage);
        Assert.Equal(PlayStatus.Played, progress.Status);
    }

    [Theory]
    [InlineData(1, 4, 25, PlayStatus.Played)]
    [InlineData(4, 4, 100, PlayStatus.Mastered)]
    public void From_CountsUnlockedOutOfTotal(int unlocked, int total, double percentage, PlayStatus status)
    {
        var progress = AchievementProgress.From(unlocked, total);

        Assert.True(progress.HasAchievements);
        Assert.Equal(unlocked, progress.Unlocked);
        Assert.Equal(total, progress.Total);
        Assert.Equal(percentage, progress.Percentage);
        Assert.Equal(status, progress.Status);
    }

    [Fact]
    public void TheAchievementCaches_KeepTheirFormat()
    {
        // Written by the former Steam and EA records: read back as is, never asked again.
        const string Saved = """{"10":{"Unlocked":3,"Total":4,"LastPlayed":"2026-09-30T20:00:00+00:00"}}""";
        var entries = new Dictionary<string, AchievementEntry>
        {
            ["10"] = new(3, 4, new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero))
        };

        Assert.Equal(Saved, JsonSerializer.Serialize(entries, SteamJsonContext.Default.DictionaryStringAchievementEntry));
        Assert.Equal(Saved, JsonSerializer.Serialize(entries, EaJsonContext.Default.DictionaryStringAchievementEntry));
        Assert.Equal(entries["10"], JsonSerializer.Deserialize(Saved, SteamJsonContext.Default.DictionaryStringAchievementEntry)!["10"]);
    }
}
