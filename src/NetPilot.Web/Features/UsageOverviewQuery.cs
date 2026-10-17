using NetPilot.Core.Budget;
using NetPilot.Core.Devices;
using NetPilot.Core.Health;
using NetPilot.Core.Usage;

namespace NetPilot.Web.Features;

public sealed record UsageOverview(UsageReadings Readings, HouseholdBudget? Budget, BillingCycle? Cycle,
    UsageReport? CycleUsage, BudgetEstimate? Forecast, AgentHeartbeat? Heartbeat, LocalTrafficStatus LanStatus)
{
    public string Confidence => LanStatus.UnavailableSinceUtc is not null
        ? "LAN source unavailable; router bytes may include LAN traffic."
        : !LanStatus.IsConfigured ? "LAN source not configured; router bytes may include LAN traffic."
        : LanStatus.LastReadAtUtc is null ? "LAN source configured; awaiting its first successful read."
        : "LAN subtraction active; live counter accuracy and LAN paths still require verification.";
}

public sealed class UsageOverviewQuery(IUsageStore usage, IDeviceStore devices, IHouseholdBudgetStore budgets,
    IAgentHeartbeatStore heartbeats, ILocalTrafficStore localTraffic, UsagePeriodClock clock, IConfiguration configuration)
{
    public async Task<UsageOverview> LoadAsync(CancellationToken ct)
    {
        var readings = new UsageReadings(await devices.GetAllAsync(ct), await usage.GetAllStatesAsync(ct),
            await usage.GetAllDailyHistoryAsync(ct), await usage.GetAllHistoryAsync(ct), clock.TodayKey());
        var budget = await budgets.GetAsync(ct);
        var heartbeat = await heartbeats.GetAsync(ct);
        var lan = await localTraffic.GetStatusAsync(ct);
        if (!string.IsNullOrWhiteSpace(configuration["NetPilot:LocalTraffic:Url"])) lan = lan with { IsConfigured = true };
        var cycle = budget is null ? null : BillingCycle.At(clock.GetUtcNow(), budget.BillingDay, clock);
        var report = cycle is null ? null : HouseholdUsage.Report(readings, new(cycle.Start, cycle.EndExclusive));
        var estimate = BudgetForecast.ForCycle(new(budget, readings, cycle, report, heartbeat, lan), clock);
        return new(readings, budget, cycle, report, estimate, heartbeat, lan);
    }

}
