using System.Net;
using NetPilot.Abstractions;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

/// <summary>Persisted directional baselines are shared by Agent and manual Web refreshes.</summary>
public sealed class LocalTrafficTracker(ILocalTrafficSource source, ILocalTrafficStore store,
    IActivityLogStore activityLog, UsagePeriodClock clock)
{
    public async Task<IReadOnlyDictionary<string, long>?> ReadDeltasAsync(
        IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        LocalTrafficReading? reading;
        try
        {
            reading = await source.ReadAsync(ct);
            if (reading is not null) Validate(reading);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Sources are optional, including third-party implementations. Never expose their
            // exception text: HTTP errors can contain URLs or credentials.
            await RecordFailureAsync(ct);
            return null;
        }

        if (reading is null)
        {
            await store.SaveStatusAsync(new LocalTrafficStatus(), ct);
            return null;
        }

        return await AccumulateAsync(reading, snapshots, ct);
    }

    private async Task<IReadOnlyDictionary<string, long>> AccumulateAsync(LocalTrafficReading reading,
        IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        var previous = (await store.GetFlowsAsync(ct)).ToDictionary(f => f.Key, f => f.Bytes);
        var macsByIp = snapshots.Where(s => IPAddress.TryParse(s.IpAddress, out _))
            .GroupBy(s => IPAddress.Parse(s.IpAddress).ToString())
            .ToDictionary(g => g.Key, g => g.Select(s => new MacAddress(s.MacAddress).ToString()).Distinct().ToArray());
        var deltas = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var flow in reading.Flows)
        {
            if (!previous.TryGetValue(flow.Key, out var last)) continue; // Long-lived exporter: baseline only.
            var delta = flow.Bytes >= last ? flow.Bytes - last : flow.Bytes;
            if (flow.Bytes < last)
                await activityLog.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.LanCounterReset, null,
                    $"LAN counter reset: {flow.SrcIp} → {flow.DstIp} (was {last} bytes, now {flow.Bytes})"), ct);
            var endpoints = new[] { flow.SrcIp, flow.DstIp }.Distinct()
                .SelectMany(ip => macsByIp.GetValueOrDefault(ip, [])).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var mac in endpoints)
                deltas[mac] = SaturatingAdd(deltas.GetValueOrDefault(mac), delta);
        }
        await store.SaveFlowsAsync(reading.Flows, ct);
        var status = await store.GetStatusAsync(ct);
        await store.SaveStatusAsync(status with { IsConfigured = true, LastReadAtUtc = clock.GetUtcNow(),
            GeneratedAtUtc = reading.GeneratedAtUtc, UnavailableSinceUtc = null }, ct);
        return deltas;
    }

    private async Task RecordFailureAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (await store.TryRecordFailureAsync(now, ct))
            await activityLog.AppendAsync(new(now, ActivityEventType.LanSourceUnavailable, null,
                "Warning: LAN traffic source unavailable; using router bytes without LAN subtraction."), ct);
    }

    private static void Validate(LocalTrafficReading reading)
    {
        var keys = new HashSet<string>();
        foreach (var flow in reading.Flows)
            if (flow.Bytes < 0 || !IPAddress.TryParse(flow.SrcIp, out var src) ||
                !IPAddress.TryParse(flow.DstIp, out var dst) || src.ToString() != flow.SrcIp ||
                dst.ToString() != flow.DstIp || !keys.Add(flow.Key))
                throw new InvalidDataException("Invalid or duplicate LAN flow.");
    }

    internal static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
}
