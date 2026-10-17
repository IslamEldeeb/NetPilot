using LiteDB;
using NetPilot.Core.Budget;

namespace NetPilot.Data;

public sealed class LiteHouseholdBudgetStore(NetPilotDatabase db) : IHouseholdBudgetStore
{
    private readonly ILiteCollection<BudgetDocument> _budgets = db.GetCollection<BudgetDocument>("household_budget");
    public Task<HouseholdBudget?> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var saved = _budgets.FindById("household");
        return Task.FromResult(saved is null ? null : new HouseholdBudget(saved.MonthlyBytes, saved.BillingDay));
    }
    public Task SaveAsync(HouseholdBudget? budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        budget?.Validate();
        if (budget is null) _budgets.Delete("household");
        else _budgets.Upsert(new BudgetDocument { MonthlyBytes = budget.MonthlyBytes, BillingDay = budget.BillingDay });
        return Task.CompletedTask;
    }
    public sealed class BudgetDocument
    {
        [BsonId] public string Id { get; set; } = "household";
        public decimal MonthlyBytes { get; set; }
        public int BillingDay { get; set; } = 1;
    }
}
