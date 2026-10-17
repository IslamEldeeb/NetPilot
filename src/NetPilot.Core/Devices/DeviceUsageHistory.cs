using System.Globalization;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Devices;

public sealed record DeviceUsageDay(string DayKey, long? InternetBytes, long? LanBytes);

public static class DeviceUsageHistory
{
    // Missing days stay missing; the current day must match the live state's period key.
    public static IReadOnlyList<DeviceUsageDay> Last30Days(MacAddress mac, string today,
        DeviceUsageState? state, IReadOnlyList<UsageDailyHistoryEntry> history)
    {
        var end = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var byDay = history.Where(h => h.Mac == mac).ToDictionary(h => h.DayKey);
        return Enumerable.Range(0, 30).Select(offset =>
        {
            var key = end.AddDays(offset - 29).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (key == today && state?.CurrentDayKey == today)
                return new DeviceUsageDay(key, state.CurrentDayBytes, state.CurrentDayLanBytes);
            return byDay.TryGetValue(key, out var day)
                ? new DeviceUsageDay(key, day.TotalBytes, day.LanBytes) : new DeviceUsageDay(key, null, null);
        }).ToArray();
    }
}
