namespace NetPilot.Core.Usage;

public enum UsagePeriodType { Day, Month }

/// <summary>
/// Pure aggregation over already-loaded usage data — blends the live running counter (for
/// the current day/month) with finalized history (for past periods), keyed by MAC. No I/O;
/// callers own fetching state/history once per page load. UTC throughout, matching
/// UsageTrackingService's month/day keys — the caller must derive periodKey and
/// isCurrentPeriod from DateTimeOffset.UtcNow, never local time, or today's totals will
/// silently read as zero near the UTC day/month boundary.
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
}
