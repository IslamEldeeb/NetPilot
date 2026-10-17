using System.Globalization;
using NetPilot.Core.Usage;
using NetPilot.Core.Health;

namespace NetPilot.Core.Budget;

public sealed record BudgetEstimate(decimal AverageBytesPerDay, decimal DaysRemaining, DateOnly EstimatedExhaustion, int SampleDays);
public sealed record BudgetForecastContext(HouseholdBudget? Budget, UsageReadings Readings,
    BillingCycle? Cycle, UsageReport? CycleUsage, AgentHeartbeat? Heartbeat, LocalTrafficStatus Lan);
public static class BudgetForecast
{
    public static BudgetEstimate? ForCycle(BudgetForecastContext context, UsagePeriodClock clock)
    {
        var heartbeat = context.Heartbeat;
        if (context.Budget is null || context.Cycle is null || context.CycleUsage is null || heartbeat is null
            || AgentHealth.Evaluate(heartbeat, clock.GetUtcNow()) != AgentHealthStatus.Healthy
            || heartbeat.ConsecutiveFailedTicks > 0 || heartbeat.LastErrorSummary is not null
            || heartbeat.LastSuccessfulRouterReadUtc is not { } lastRead || lastRead > clock.GetUtcNow()
            || clock.GetUtcNow() - lastRead > TimeSpan.FromSeconds(2d * heartbeat.TickIntervalSeconds + 60)
            || context.Lan.UnavailableSinceUtc is not null || (context.Lan.IsConfigured && context.Lan.LastReadAtUtc is null)) return null;
        var today = DateOnly.ParseExact(context.Readings.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var elapsed = HouseholdUsage.Days(context.Readings, new(context.Cycle.Start, today.AddDays(1)));
        if (elapsed.Any(day => day.InternetBytes is null)) return null;
        return Estimate(context.Budget, context.CycleUsage.InternetBytes, ValidDays(context.Readings, clock), today);
    }

    public static BudgetEstimate? Estimate(HouseholdBudget budget, decimal usedBytes,
        IReadOnlyList<decimal> validDailyBytes, DateOnly today)
    {
        budget.Validate();
        if (validDailyBytes.Count is < 3 or > 7 || validDailyBytes.Any(bytes => bytes < 0)) return null;
        var average = validDailyBytes.Average();
        if (average <= 0) return null;
        var remaining = Math.Max(0, budget.MonthlyBytes - usedBytes) / average;
        if (remaining > DateOnly.MaxValue.DayNumber - today.DayNumber) return null;
        return new(average, remaining, today.AddDays((int)decimal.Ceiling(remaining)), validDailyBytes.Count);
    }

    public static IReadOnlyList<decimal> ValidDays(UsageReadings readings, UsagePeriodClock clock)
    {
        var today = DateOnly.ParseExact(readings.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return readings.Daily
            .GroupBy(day => day.DayKey).Where(group =>
            {
                if (!DateOnly.TryParseExact(group.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
                return date >= today.AddDays(-7) && date < today
                    && group.All(day => day.ForecastEligible && day.TotalBytes >= 0 && clock.DayKey(day.FinalizedAtUtc) == date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }).Select(group => group.Sum(day => (decimal)day.TotalBytes)).ToArray();
    }
}
