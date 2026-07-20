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
/// </summary>
public class UsageTrackingService(IUsageStore usageStore, IActivityLogStore activityLog)
{
    public async Task TrackAsync(IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var monthKey = MonthKeyFor(now);
        var dayKey = DayKeyFor(now);

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Usage is null)
                continue; // provider doesn't support usage tracking, or couldn't parse this tick

            var mac = new MacAddress(snapshot.MacAddress);
            var state = await usageStore.FindStateAsync(mac, ct)
                ?? new DeviceUsageState { Mac = mac, CurrentMonthKey = monthKey, CurrentDayKey = dayKey };

            if (state.CurrentMonthKey != monthKey)
            {
                if (!string.IsNullOrEmpty(state.CurrentMonthKey))
                {
                    await usageStore.AppendHistoryAsync(
                        new UsageHistoryEntry(mac, state.CurrentMonthKey, state.CurrentMonthBytes, now), ct);
                }
                state.CurrentMonthKey = monthKey;
                state.CurrentMonthBytes = 0;
            }

            if (state.CurrentDayKey != dayKey)
            {
                if (!string.IsNullOrEmpty(state.CurrentDayKey))
                {
                    await usageStore.AppendDailyHistoryAsync(
                        new UsageDailyHistoryEntry(mac, state.CurrentDayKey, state.CurrentDayBytes, now), ct);
                }
                state.CurrentDayKey = dayKey;
                state.CurrentDayBytes = 0;
            }

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
    }

    private static string MonthKeyFor(DateTimeOffset utc) => utc.ToString("yyyy-MM");
    private static string DayKeyFor(DateTimeOffset utc) => utc.ToString("yyyy-MM-dd");
}
