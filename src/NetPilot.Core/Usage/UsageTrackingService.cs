using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;

namespace NetPilot.Core.Usage;

/// <summary>
/// Turns the router's raw, reset-prone usage counter into a durable running monthly total.
/// Reset detection is per-device and cause-agnostic — see phase2-usage-tracking-plan.md §3:
/// a counter that's lower than last observed is treated as a reset regardless of why.
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
                // First time we've ever seen this device's counter. Do NOT add `current` as
                // usage for this month/day — it's likely a pre-existing lifetime/session total,
                // not usage that happened in this period. Adopt it as the baseline only.
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
