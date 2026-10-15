using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public enum UsagePeriodType { Day, Month }

/// <summary>
/// Pure aggregation over already-loaded usage data — blends the live running counter (for
/// the current day/month) with finalized history (for past periods), keyed by MAC. No I/O;
/// callers own fetching state/history once per page load. Derive periodKey and
/// isCurrentPeriod with UsagePeriodClock so comparisons match the Agent's configured
/// timezone (Africa/Cairo by default), including near day/month boundaries.
/// </summary>
public static class UsageQuery
{
    /// <summary>
    /// Bytes used per MAC for the given period. The returned map is the single source for
    /// both a "total across all devices" figure and a per-device breakdown — summing its
    /// values and summing its rows always agree, including for MACs no longer in the device
    /// list (a removed/guest device still shows up here rather than silently vanishing from
    /// one view but not the other).
    /// </summary>
    public static IReadOnlyDictionary<string, long> BytesByDevice(
        UsagePeriodType periodType,
        string periodKey,
        bool isCurrentPeriod,
        IReadOnlyList<DeviceUsageState> liveStates,
        IReadOnlyList<UsageHistoryEntry> monthlyHistory,
        IReadOnlyList<UsageDailyHistoryEntry> dailyHistory)
    {
        if (isCurrentPeriod)
        {
            // A device the Agent hasn't polled this period (offline, or Agent was down across
            // the boundary) keeps a stale live counter from whatever period it last rolled
            // over into — only include it if its own key actually matches what's being asked
            // for, otherwise a device idle since last month would leak last month's total into
            // this month's view.
            return periodType == UsagePeriodType.Day
                ? liveStates.Where(s => s.CurrentDayKey == periodKey).ToDictionary(s => (string)s.Mac, s => s.CurrentDayBytes)
                : liveStates.Where(s => s.CurrentMonthKey == periodKey).ToDictionary(s => (string)s.Mac, s => s.CurrentMonthBytes);
        }

        return periodType == UsagePeriodType.Day
            ? dailyHistory.Where(h => h.DayKey == periodKey).ToDictionary(h => (string)h.Mac, h => h.TotalBytes)
            : monthlyHistory.Where(h => h.MonthKey == periodKey).ToDictionary(h => (string)h.Mac, h => h.TotalBytes);
    }

    /// <summary>Excluded LAN bytes, matched to router deltas (including settled carry), for the same period.</summary>
    public static IReadOnlyDictionary<string, long> LanBytesByDevice(
        UsagePeriodType periodType, string periodKey, bool isCurrentPeriod,
        IReadOnlyList<DeviceUsageState> liveStates, IReadOnlyList<UsageHistoryEntry> monthlyHistory,
        IReadOnlyList<UsageDailyHistoryEntry> dailyHistory)
    {
        if (isCurrentPeriod)
            return periodType == UsagePeriodType.Day
                ? liveStates.Where(s => s.CurrentDayKey == periodKey).ToDictionary(s => (string)s.Mac, s => s.CurrentDayLanBytes)
                : liveStates.Where(s => s.CurrentMonthKey == periodKey).ToDictionary(s => (string)s.Mac, s => s.CurrentMonthLanBytes);
        return periodType == UsagePeriodType.Day
            ? dailyHistory.Where(h => h.DayKey == periodKey).ToDictionary(h => (string)h.Mac, h => h.LanBytes)
            : monthlyHistory.Where(h => h.MonthKey == periodKey).ToDictionary(h => (string)h.Mac, h => h.LanBytes);
    }

    /// <summary>
    /// Rolls a BytesByDevice result up to one total per device category, for a "top categories"
    /// view. A MAC with no matching device (removed, or usage history for a device that no
    /// longer exists) is grouped under DeviceCategory.UnknownKey rather than dropped, so the
    /// category total always accounts for every byte BytesByDevice reported.
    /// </summary>
    public static IReadOnlyDictionary<string, long> BytesByCategory(
        IReadOnlyDictionary<string, long> bytesByMac, IReadOnlyList<Device> devices)
    {
        var categoryByMac = devices.ToDictionary(d => (string)d.Mac, d => d.CategoryKey);
        var totals = new Dictionary<string, long>();

        foreach (var (mac, bytes) in bytesByMac)
        {
            var categoryKey = categoryByMac.GetValueOrDefault(mac, DeviceCategory.UnknownKey);
            totals[categoryKey] = totals.GetValueOrDefault(categoryKey) + bytes;
        }

        return totals;
    }
}
