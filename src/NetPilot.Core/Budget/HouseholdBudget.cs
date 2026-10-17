using System.Globalization;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Budget;

public sealed record HouseholdBudget(decimal MonthlyBytes, int BillingDay = 1)
{
    public void Validate()
    {
        if (MonthlyBytes <= 0 || MonthlyBytes > long.MaxValue || MonthlyBytes != decimal.Truncate(MonthlyBytes))
            throw new ArgumentException("Budget must be positive whole bytes within the supported range.");
        if (BillingDay is < 1 or > 31) throw new ArgumentException("Billing day must be from 1 to 31.");
    }
}

public interface IHouseholdBudgetStore
{
    Task<HouseholdBudget?> GetAsync(CancellationToken ct);
    Task SaveAsync(HouseholdBudget? budget, CancellationToken ct);
}

public sealed record BillingCycle(DateOnly Start, DateOnly EndExclusive)
{
    // Each boundary is clamped independently: Jan 31 → Feb 28/29 → Mar 31.
    public static BillingCycle At(DateTimeOffset instant, int billingDay, UsagePeriodClock clock)
    {
        if (billingDay is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(billingDay));
        var today = DateOnly.ParseExact(clock.DayKey(instant), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var month = new DateOnly(today.Year, today.Month, 1);
        if (today < Boundary(month, billingDay)) month = month.AddMonths(-1);
        return new(Boundary(month, billingDay), Boundary(month.AddMonths(1), billingDay));
    }

    private static DateOnly Boundary(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
}
