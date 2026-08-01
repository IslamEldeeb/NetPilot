using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;

namespace NetPilot.Core.Usage;

/// <summary>
/// Turns the router's raw, reset-prone usage counter into a durable running monthly total.
/// Reset detection is per-device and cause-agnostic — see phase2-usage-tracking-plan.md §3:
/// a counter that's lower than last observed is treated as a reset regardless of why. The
/// very first reading NetPilot ever sees for a device is counted in full immediately, the
/// same as a detected reset, rather than held back as a baseline — on the assumption that
/// this counter resets on a short cadence (daily / on restart). That assumption comes from
/// resets the user has observed in the TP-Link mobile app, which most likely reads TP-Link
/// Cloud rather than this same local `trafficUsage` field — so the cadence for the specific
/// counter polled here is still not live-verified (phase2-usage-tracking-plan.md open item
/// #1). If a device shows an implausible spike on its very first day after this ships, that's
/// the signal this counter is actually long-lived and this behavior should revert to
/// baseline-only for the first observation.
/// Called once per tick with the same snapshot PolicyReconciliationService already fetched
/// — no extra HTTP call to the router.
/// Month/day bucket boundaries are computed in <paramref name="timeZone"/>, not UTC — all
/// other timestamps (LastPollAtUtc, FinalizedAtUtc, activity log entries) stay UTC as before;
/// only "which month/day bucket does this reading belong to" uses local time, so totals line
/// up with the user's own calendar instead of rolling over a few hours off at UTC midnight.
/// </summary>
public class UsageTrackingService(IUsageStore usageStore, IActivityLogStore activityLog, TimeZoneInfo timeZone)
{
    public async Task TrackAsync(IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, timeZone);
        var monthKey = MonthKeyFor(local);
        var dayKey = DayKeyFor(local);
        var seenMacs = new HashSet<string>();

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Usage is null)
                continue; // provider doesn't support usage tracking, or couldn't parse this tick

            var mac = new MacAddress(snapshot.MacAddress);
            seenMacs.Add(mac);
            var state = await usageStore.FindStateAsync(mac, ct)
                ?? new DeviceUsageState { Mac = mac, CurrentMonthKey = monthKey, CurrentDayKey = dayKey };

            await RollBucketsAsync(state, monthKey, dayKey, now, ct);

            var current = snapshot.Usage.TotalBytes;

            if (state.LastPollAtUtc is null)
            {
                // First time we've ever seen this device's counter — counted in full, assuming
                // it resets often enough (daily / on restart) that this isn't a stale
                // multi-month lifetime total. See class doc: that assumption is not yet
                // live-verified for this specific counter. No reset event logged here: nothing
                // actually reset, we just started watching.
                state.CurrentMonthBytes += current;
                state.CurrentDayBytes += current;
            }
            else if (current >= state.LastRawCounterBytes)
            {
                var delta = current - state.LastRawCounterBytes;
                state.CurrentMonthBytes += delta;
                state.CurrentDayBytes += delta;
            }
            else
            {
                // Counter went backwards — reset, cause unknown and irrelevant (restart, daily
                // rollover, whatever). Assume it restarted at 0 and resume from here.
                state.CurrentMonthBytes += current;
                state.CurrentDayBytes += current;
                await activityLog.AppendAsync(new ActivityLogEntry(now, ActivityEventType.UsageCounterReset, mac,
                    $"Usage counter reset detected (was {state.LastRawCounterBytes} bytes, now {current}) — resumed accumulation from new baseline"), ct);
            }

            state.LastRawCounterBytes = current;
            state.LastPollAtUtc = now;
            await usageStore.UpsertStateAsync(state, ct);
        }

        await FinalizeStaleStatesAsync(monthKey, dayKey, now, seenMacs, ct);
    }

    /// <summary>
    /// Finalizes a device's running total into history when its stored bucket key no longer
    /// matches the current one — same rollover the per-snapshot path always did, factored out
    /// so <see cref="FinalizeStaleStatesAsync"/> can apply it to devices this tick never saw.
    /// </summary>
    private async Task RollBucketsAsync(DeviceUsageState state, string monthKey, string dayKey, DateTimeOffset now, CancellationToken ct)
    {
        if (state.CurrentMonthKey != monthKey)
        {
            if (!string.IsNullOrEmpty(state.CurrentMonthKey))
            {
                await usageStore.AppendHistoryAsync(
                    new UsageHistoryEntry(state.Mac, state.CurrentMonthKey, state.CurrentMonthBytes, now), ct);
            }
            state.CurrentMonthKey = monthKey;
            state.CurrentMonthBytes = 0;
        }

        if (state.CurrentDayKey != dayKey)
        {
            if (!string.IsNullOrEmpty(state.CurrentDayKey))
            {
                await usageStore.AppendDailyHistoryAsync(
                    new UsageDailyHistoryEntry(state.Mac, state.CurrentDayKey, state.CurrentDayBytes, now), ct);
            }
            state.CurrentDayKey = dayKey;
            state.CurrentDayBytes = 0;
        }
    }

    /// <summary>
    /// Closes out any device's trailing period even if it never appears in a snapshot again —
    /// e.g. a device that's permanently removed from the network. Without this, its last
    /// period's total stayed frozen in usage_state forever: not lost, but invisible in both the
    /// current-period and history views, since neither reads a live state row for a MAC that
    /// stopped reporting. Mirrors PolicyReconciliationService.MarkMissingDevicesOfflineAsync's
    /// pattern of sweeping all known entities each tick, not just ones in the current snapshot.
    /// </summary>
    private async Task FinalizeStaleStatesAsync(string monthKey, string dayKey, DateTimeOffset now, HashSet<string> seenMacs, CancellationToken ct)
    {
        var allStates = await usageStore.GetAllStatesAsync(ct);
        foreach (var state in allStates)
        {
            if (seenMacs.Contains(state.Mac))
                continue; // already rolled above this tick

            if (state.CurrentMonthKey == monthKey && state.CurrentDayKey == dayKey)
                continue; // not stale

            await RollBucketsAsync(state, monthKey, dayKey, now, ct);
            await usageStore.UpsertStateAsync(state, ct);
        }
    }

    private static string MonthKeyFor(DateTimeOffset local) => local.ToString("yyyy-MM");
    private static string DayKeyFor(DateTimeOffset local) => local.ToString("yyyy-MM-dd");
}
