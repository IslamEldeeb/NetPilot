using NetPilot.Core.Budget;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Budget;

public class HouseholdBudgetTests
{
    [Theory]
    [InlineData("2025-02-28T12:00:00Z", 31, "2025-02-28", "2025-03-31")]
    [InlineData("2024-02-29T12:00:00Z", 31, "2024-02-29", "2024-03-31")]
    [InlineData("2026-04-30T12:00:00Z", 31, "2026-04-30", "2026-05-31")]
    [InlineData("2026-03-31T12:00:00Z", 31, "2026-03-31", "2026-04-30")]
    [InlineData("2026-03-30T12:00:00Z", 31, "2026-02-28", "2026-03-31")]
    [InlineData("2026-07-30T20:59:59Z", 31, "2026-06-30", "2026-07-31")]
    [InlineData("2026-07-30T21:00:00Z", 31, "2026-07-31", "2026-08-31")]
    [InlineData("2026-01-30T21:59:59Z", 31, "2025-12-31", "2026-01-31")]
    [InlineData("2026-01-30T22:00:00Z", 31, "2026-01-31", "2026-02-28")]
    [InlineData("2026-10-16T12:00:00Z", 28, "2026-09-28", "2026-10-28")]
    [InlineData("2026-10-16T12:00:00Z", 29, "2026-09-29", "2026-10-29")]
    [InlineData("2026-10-16T12:00:00Z", 30, "2026-09-30", "2026-10-30")]
    public void Cycle_ClampsEachBoundary_UsingCairoDate(string instant, int day, string start, string end)
    {
        var cycle = BillingCycle.At(DateTimeOffset.Parse(instant), day, new());
        Assert.Equal(DateOnly.Parse(start), cycle.Start);
        Assert.Equal(DateOnly.Parse(end), cycle.EndExclusive);
    }

    [Fact]
    public void Forecast_214Of300At17Point2_EstimatesFiveDays_NotCalendarQuotaRollover()
    {
        var estimate = BudgetForecast.Estimate(new(300_000_000_000), 214_000_000_000,
            [17_200_000_000, 17_200_000_000, 17_200_000_000], new(2026, 10, 16));
        Assert.NotNull(estimate);
        Assert.Equal(5, estimate.DaysRemaining);
        Assert.Equal(new(2026, 10, 21), estimate.EstimatedExhaustion);
    }

    [Theory]
    [InlineData(0, 17)]
    [InlineData(2, 17)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    public void Forecast_InsufficientOrInvalidSamples_SuppressesEstimate(int count, int bytes)
    {
        Assert.Null(BudgetForecast.Estimate(new(300), 214, Enumerable.Repeat((decimal)bytes, count).ToArray(), new(2026, 10, 16)));
    }

    [Fact]
    public void Forecast_UsesOnlyEligibleCompletedDaysInLastSevenDays()
    {
        var clock = new UsagePeriodClock();
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        var readings = new UsageReadings([], [], [
            new(mac, "2026-10-08", 99, DateTimeOffset.Parse("2026-10-09T12:00:00Z"), ForecastEligible: true),
            new(mac, "2026-10-09", 10, DateTimeOffset.Parse("2026-10-10T12:00:00Z"), ForecastEligible: true),
            new(mac, "2026-10-10", 20, DateTimeOffset.Parse("2026-10-11T12:00:00Z")),
            new(mac, "2026-10-11", 30, DateTimeOffset.Parse("2026-10-13T12:00:00Z"), ForecastEligible: true),
            new(mac, "2026-10-16", 40, DateTimeOffset.Parse("2026-10-16T12:00:00Z"), ForecastEligible: true)
        ], [], "2026-10-16");
        Assert.Equal([10m], BudgetForecast.ValidDays(readings, clock));
    }

    [Theory]
    [InlineData(false, "2026-10-15T21:03:00Z", true)]
    [InlineData(true, "2026-10-15T21:03:00Z", false)]
    [InlineData(false, "2026-10-15T22:03:00Z", false)]
    public void SamplingCoverage_RejectsGapsAndPartialFirstDays(bool gap, string first, bool expected)
    {
        var state = new DeviceUsageState { Mac = new("AA-BB-CC-DD-EE-01"), CurrentDayKey = "2026-10-16",
            DayFirstPollAtUtc = DateTimeOffset.Parse(first), DayHasSamplingGap = gap,
            LastPollAtUtc = DateTimeOffset.Parse("2026-10-16T20:58:00Z") };
        Assert.Equal(expected, UsageSampleCoverage.IsComplete(state, DateTimeOffset.Parse("2026-10-16T21:02:00Z"), new()));
    }
}
