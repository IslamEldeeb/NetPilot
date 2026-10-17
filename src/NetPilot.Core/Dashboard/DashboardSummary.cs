using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Health;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Dashboard;

public sealed record DashboardNotice(string Text, string Href);
public sealed record DashboardInputs(UsageReadings Usage, AgentHeartbeat? Heartbeat, DateTimeOffset Now,
    IReadOnlyList<MacAddress> Protected, IReadOnlyList<ActionClaim> Claims,
    IReadOnlyList<QuotaPeriodState> QuotaStates, IReadOnlyList<RuleFiring> Firings);

public static class DashboardSummary
{
    public static IReadOnlyList<DashboardNotice> Attention(DashboardInputs snapshot)
    {
        var notices = new List<DashboardNotice>();
        var health = AgentHealth.Evaluate(snapshot.Heartbeat, snapshot.Now);
        if (health != AgentHealthStatus.Healthy)
            notices.Add(new($"Agent {health}: actions and totals may be delayed.", "/#network-health"));
        foreach (var device in snapshot.Usage.Devices)
        {
            var link = $"/devices/{device.Mac}";
            var name = HouseholdUsage.DeviceName(device);
            if (device.IsBlocked) notices.Add(new($"{name}: recorded blocked state.", link));
            if (device.QuotaThrottle is not null) notices.Add(new($"{name}: automatic throttle recorded.", link + "#restrictions"));
            if (snapshot.Protected.Contains(device.Mac)) notices.Add(new($"{name}: protected from automatic restrictions.", link));
            if (device.FirstSeenAtUtc <= snapshot.Now && device.FirstSeenAtUtc >= snapshot.Now.AddDays(-1))
                notices.Add(new($"{name}: newly discovered; review device.", link));
        }
        foreach (var state in snapshot.QuotaStates.Where(state => state.Percent >= 80 &&
            state.PeriodKey == (state.PeriodKind == QuotaPeriodKind.Day ? snapshot.Usage.Today : snapshot.Usage.Today[..7])))
            notices.Add(new($"{state.Mac}: {state.PeriodKind} quota {state.Percent:0}% used.", $"/devices/{state.Mac}"));
        foreach (var firing in snapshot.Firings.Where(firing => firing.FiredAtUtc >= snapshot.Now.AddDays(-1)).OrderByDescending(firing => firing.FiredAtUtc).Take(10))
            notices.Add(new($"Rule {firing.RuleName}: {firing.Outcome}.", "/automations/rules"));
        return notices;
    }

    public static IReadOnlyList<DashboardNotice> Restrictions(DashboardInputs snapshot, UsagePeriodClock clock)
    {
        var restrictions = snapshot.Usage.Devices.Where(device => device.IsBlocked || device.QuotaThrottle is not null
            || device.Override is { Enabled: true } || device.RouterReportedLimit is { Enabled: true })
            .Select(device => new DashboardNotice($"{HouseholdUsage.DeviceName(device)}: recorded block {device.IsBlocked}; automatic throttle {device.QuotaThrottle?.DownloadKbps?.ToString() ?? "none"} Kbps. Manual/base/router limits are shown separately on device detail.",
                $"/devices/{device.Mac}#restrictions")).ToList();
        restrictions.AddRange(snapshot.Claims.Select(claim => new DashboardNotice(
            $"{claim.Mac}: {claim.Kind} · {claim.Source} · {(claim.DryRun ? "dry-run preview" : claim.Pending ? "pending next tick" : "active claim")}" +
            (claim.ExpiresAtUtc is { } expiry ? $" · expires {TimeZoneInfo.ConvertTime(expiry, clock.TimeZone):g} · {clock.TimeZone.Id} (released on next tick)" : ""), $"/devices/{claim.Mac}#restrictions")));
        return restrictions;
    }
}
