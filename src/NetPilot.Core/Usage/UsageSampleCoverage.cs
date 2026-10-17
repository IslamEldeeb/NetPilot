using System.Globalization;
namespace NetPilot.Core.Usage;

public static class UsageSampleCoverage
{
    // Conservative bound, also for custom slow polling: such days remain totals, not forecast samples.
    public static readonly TimeSpan MaximumGap = TimeSpan.FromMinutes(15);
    public static bool IsComplete(DeviceUsageState state, DateTimeOffset finalizedAt, UsagePeriodClock clock)
    {
        if (state.DayHasSamplingGap || state.DayFirstPollAtUtc is not { } first || state.LastPollAtUtc is not { } last
            || finalizedAt < last || finalizedAt - last > MaximumGap) return false;
        var localFirst = TimeZoneInfo.ConvertTime(first, clock.TimeZone);
        if (localFirst.TimeOfDay > MaximumGap || clock.DayKey(first) != state.CurrentDayKey) return false;
        return DateOnly.ParseExact(state.CurrentDayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == clock.DayKey(finalizedAt);
    }
}
