using NetPilot.Core.Budget;
using NetPilot.Core.Devices;
using NetPilot.Core.Health;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Budget;

public class ForecastReliabilityTests
{
    [Theory]
    [InlineData("healthy", true)]
    [InlineData("outage", false)]
    [InlineData("agent-down", false)]
    [InlineData("stale-router", false)]
    [InlineData("incomplete-cycle", false)]
    [InlineData("failed-tick", false)]
    [InlineData("low-sample", false)]
    public void CycleForecast_RequiresFreshReadsCompletedSamplesAndKnownCycleUsage(string scenario, bool expected)
    {
        var now = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
        var clock = new UsagePeriodClock(timeProvider: new FakeTimeProvider(now));
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        var daily = Enumerable.Range(9, 7).Select(day => new UsageDailyHistoryEntry(mac, $"2026-10-{day:00}", 10_000_000_000,
            DateTimeOffset.Parse($"2026-10-{day + 1:00}T00:03:00+03:00"), ForecastEligible: scenario != "low-sample" || day == 15)).ToArray();
        var readings = new UsageReadings([], [new() { Mac = mac, CurrentDayKey = clock.TodayKey(), CurrentDayBytes = 5_000_000_000 }],
            daily, [], clock.TodayKey());
        var cycle = new BillingCycle(new(2026, 10, scenario == "incomplete-cycle" ? 1 : 9), new(2026, 11, 9));
        var heartbeat = new AgentHeartbeat { LastTickStartedUtc = now.AddSeconds(-5), LastTickCompletedUtc = now,
            LastSuccessfulRouterReadUtc = scenario == "stale-router" ? now.AddHours(-1) : now, TickIntervalSeconds = 180,
            ConsecutiveFailedTicks = scenario == "failed-tick" ? 1 : 0 };
        if (scenario == "agent-down") heartbeat = heartbeat with { LastTickStartedUtc = now.AddHours(-1), LastTickCompletedUtc = now.AddHours(-1) };
        var lan = new LocalTrafficStatus { UnavailableSinceUtc = scenario == "outage" ? now.AddMinutes(-2) : null };
        var context = new BudgetForecastContext(new(300_000_000_000, 9), readings, cycle,
            HouseholdUsage.Report(readings, new(cycle.Start, cycle.EndExclusive)), heartbeat, lan);
        Assert.Equal(expected, BudgetForecast.ForCycle(context, clock) is not null);
    }
}
