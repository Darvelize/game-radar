namespace GameRadar.App.ViewModels;

/// <summary>"il y a 2 h" rather than "24/09/2026 à 09:12": easier to read at a glance.</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset moment, DateTimeOffset now)
    {
        var elapsed = now - moment;
        if (elapsed < TimeSpan.FromMinutes(1))
            return Strings.Time_JustNow;
        if (elapsed < TimeSpan.FromHours(1))
            return Localization.Format(Strings.Time_MinutesAgo, (int)elapsed.TotalMinutes);
        if (elapsed < TimeSpan.FromHours(24))
            return Localization.Format(Strings.Time_HoursAgo, (int)elapsed.TotalHours);

        // Calendar days, not 24 h blocks: something done at 23:00 is "hier" at 08:00.
        int days = (now.ToLocalTime().Date - moment.ToLocalTime().Date).Days;
        return days switch
        {
            1 => Strings.Time_Yesterday,
            < 7 => Localization.Format(Strings.Time_DaysAgo, days),
            _ => Localization.Format(Strings.Time_On, moment.ToLocalTime())
        };
    }
}
