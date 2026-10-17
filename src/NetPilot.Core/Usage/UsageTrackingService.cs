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
/// Day/month boundaries use the configured timezone (Africa/Cairo by default).
/// </summary>
public class UsageTrackingService(IUsageStore usageStore, IActivityLogStore activityLog, UsagePeriodClock periodClock,
    LocalTrafficTracker localTraffic, IDeviceStore deviceStore)
{
    // Bound stale/skewed exclusions so they cannot mask unlimited future Internet traffic.
    public const long MaxLanCarryBytes = 1_000_000_000;

    public async Task TrackAsync(IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        var now = periodClock.GetUtcNow();
        var monthKey = periodClock.MonthKey(now);
        var dayKey = periodClock.DayKey(now);

        var lanSample = await localTraffic.ReadSampleAsync(snapshots, ct);
        var lanDeltas = lanSample.Deltas;
        var lanOnly = (await deviceStore.GetAllAsync(ct)).Where(d => d.UsageLanOnly).Select(d => d.Mac).ToHashSet();

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
                        new UsageHistoryEntry(mac, state.CurrentMonthKey, state.CurrentMonthBytes, now, state.CurrentMonthLanBytes), ct);
                }
                state.CurrentMonthKey = monthKey;
                state.CurrentMonthBytes = 0;
                state.CurrentMonthLanBytes = 0;
            }

            if (state.CurrentDayKey != dayKey)
            {
                if (!string.IsNullOrEmpty(state.CurrentDayKey))
                {
                    await usageStore.AppendDailyHistoryAsync(
                        new UsageDailyHistoryEntry(mac, state.CurrentDayKey, state.CurrentDayBytes, now, state.CurrentDayLanBytes, UsageSampleCoverage.IsComplete(state, now, periodClock)), ct);
                }
                state.CurrentDayKey = dayKey;
                state.CurrentDayBytes = 0;
                state.CurrentDayLanBytes = 0;
                state.DayFirstPollAtUtc = null;
                state.DayHasSamplingGap = false;
            }
            state.DayFirstPollAtUtc ??= now;
            state.DayHasSamplingGap |= lanSample.HadOutage;
            if (state.LastPollAtUtc is { } lastPoll && now - lastPoll > UsageSampleCoverage.MaximumGap)
                state.DayHasSamplingGap = true;

            var current = snapshot.Usage.TotalBytes;

            long routerDelta;
            if (state.LastPollAtUtc is null)
            {
                // First time we've ever seen this device's counter — counted in full, assuming
                // it resets often enough (daily / on restart) that this isn't a stale
                // multi-month lifetime total. See class doc: that assumption is not yet
                // live-verified for this specific counter. No reset event logged here: nothing
                // actually reset, we just started watching.
                routerDelta = current;
            }
            else if (current >= state.LastRawCounterBytes)
            {
                routerDelta = current - state.LastRawCounterBytes;
            }
            else
            {
                // Counter went backwards — reset, cause unknown and irrelevant (restart, daily
                // rollover, whatever). Assume it restarted at 0 and resume from here.
                routerDelta = current;
                state.DayHasSamplingGap = true;
                await activityLog.AppendAsync(new ActivityLogEntry(now, ActivityEventType.UsageCounterReset, mac,
                    $"Usage counter reset detected (was {state.LastRawCounterBytes} bytes, now {current}) — resumed accumulation from new baseline"), ct);
            }

            AccumulateInternetUsage(state, routerDelta, lanDeltas, lanOnly);
            state.LastRawCounterBytes = current;
            state.LastPollAtUtc = now;
            await usageStore.UpsertStateAsync(state, ct);
        }
    }

    private static void AccumulateInternetUsage(DeviceUsageState state, long routerDelta,
        IReadOnlyDictionary<string, long>? lanDeltas, HashSet<MacAddress> lanOnly)
    {
        var lanBytes = 0L;
        if (lanOnly.Contains(state.Mac))
        {
            lanBytes = routerDelta;
            state.PendingLanCarryBytes = 0;
        }
        else if (lanDeltas is not null)
        {
            var available = LocalTrafficTracker.SaturatingAdd(lanDeltas.GetValueOrDefault((string)state.Mac),
                state.PendingLanCarryBytes);
            lanBytes = Math.Min(routerDelta, available);
            state.PendingLanCarryBytes = Math.Min(MaxLanCarryBytes, available - lanBytes);
        }
        // During source outages (or while disabled), even saved carry is frozen: fail closed.
        state.CurrentMonthBytes += routerDelta - lanBytes;
        state.CurrentDayBytes += routerDelta - lanBytes;
        state.CurrentMonthLanBytes += lanBytes;
        state.CurrentDayLanBytes += lanBytes;
    }
}
