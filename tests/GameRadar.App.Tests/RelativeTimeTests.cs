using GameRadar.App.ViewModels;

namespace GameRadar.App.Tests;

public sealed class RelativeTimeTests
{
    // Built in the local time zone: "hier" depends on local calendar days.
    private static readonly DateTimeOffset Now = new(
        2026, 9, 24, 8, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 24, 8, 0, 0)));

    [Theory]
    [InlineData(0, "à l'instant")]
    [InlineData(5, "il y a 5 min")]
    [InlineData(59, "il y a 59 min")]
    [InlineData(60, "il y a 1 h")]
    [InlineData(23 * 60, "il y a 23 h")]
    public void Recent_moments_count_minutes_then_hours(int minutesAgo, string expected)
    {
        Assert.Equal(expected, RelativeTime.Format(Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void Yesterday_is_about_calendar_days_not_24_hour_blocks()
    {
        // 25 h before 08:00 = yesterday at 07:00.
        Assert.Equal("hier", RelativeTime.Format(Now.AddHours(-25), Now));
        // 2 days and a bit earlier: two calendar days back.
        Assert.Equal("il y a 2 jours", RelativeTime.Format(Now.AddHours(-49), Now));
    }

    [Fact]
    public void After_a_week_the_date_is_shown()
    {
        string label = RelativeTime.Format(Now.AddDays(-10), Now);

        Assert.StartsWith("le ", label);
        Assert.Contains("2026", label);
    }
}
