using System.Globalization;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Rules;

internal record TriggerMatch(string EpisodeKey, decimal Used = 0, long Cap = 0);

internal static class RuleTriggerEvaluation
{
    public static TriggerMatch? Evaluate(Rule rule, Device? device, IReadOnlyList<DeviceUsageState> usage,
        UsagePeriodClock clock)
    {
        var now = clock.GetUtcNow();
        return rule.Trigger switch
        {
            UsageOver u when device is not null => Usage(u.Period, u.Bytes,
                usage.Where(s => s.Mac == device.Mac), clock),
            HouseholdUsageOver h => Usage(h.Period, h.Bytes, usage, clock),
            TimeWindow w => WindowEpisode(w, now, clock) is { } key ? new(key) : null,
            DeviceJoined when device is not null && device.FirstSeenAtUtc >= rule.CreatedAtUtc
                && device.FirstSeenAtUtc <= now => new("joined"),
            DeviceIdle idle when device is not null && (now - device.LastSeenAtUtc).TotalDays >= idle.Days =>
                new($"idle:{device.LastSeenAtUtc.UtcTicks}"),
            _ => null
        };
    }

    private static TriggerMatch? Usage(RulePeriod period, long cap, IEnumerable<DeviceUsageState> states, UsagePeriodClock clock)
    {
        var key = period == RulePeriod.Day ? clock.TodayKey() : clock.ThisMonthKey();
        var used = states.Where(s => (period == RulePeriod.Day ? s.CurrentDayKey : s.CurrentMonthKey) == key)
            .Sum(s => (decimal)(period == RulePeriod.Day ? s.CurrentDayBytes : s.CurrentMonthBytes));
        return used >= cap ? new($"{period.ToString().ToLowerInvariant()}:{key}", used, cap) : null;
    }

    public static string? WindowEpisode(TimeWindow window, DateTimeOffset now, UsagePeriodClock clock)
    {
        if (!Rule.TryTime(window.Start, out var start) || !Rule.TryTime(window.End, out var end)) return null;
        var local = TimeZoneInfo.ConvertTime(now, clock.TimeZone).DateTime;
        var time = TimeOnly.FromDateTime(local);
        var date = local.Date;
        if (start < end)
        {
            if (time < start || time >= end) return null;
        }
        else
        {
            if (start == end || (time < start && time >= end)) return null;
            if (time < end) date = date.AddDays(-1);
        }
        return window.Days.Contains(date.DayOfWeek) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }
}
