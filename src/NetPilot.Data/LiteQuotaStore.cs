using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Quota;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteQuotaStore(NetPilotDatabase db) : IQuotaStore
{
    private readonly ILiteCollection<QuotaPolicyDocument> _collection = db.GetCollection<QuotaPolicyDocument>("quota_policies");

    public Task<IReadOnlyList<QuotaPolicy>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QuotaPolicy>>(_collection.FindAll().Select(ToDomain).ToList());

    public Task<QuotaPolicy?> FindAsync(string id, CancellationToken ct)
    {
        var doc = _collection.FindById(id);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task UpsertAsync(QuotaPolicy policy, CancellationToken ct)
    {
        var errors = policy.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(policy));
        db.InTransaction(() =>
        {
            _collection.Upsert(ToDocument(policy));
            AutomationRevision.Advance(db, "quota:" + policy.Id);
        });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        db.InTransaction(() =>
        {
            _collection.Delete(id);
            AutomationRevision.Advance(db, "quota:" + id);
        });
        return Task.CompletedTask;
    }

    private static QuotaPolicy ToDomain(QuotaPolicyDocument doc) => new()
    {
        Id = doc.Id, Scope = doc.Scope, CategoryKey = doc.CategoryKey,
        Mac = doc.Mac is null ? null : new MacAddress(doc.Mac),
        DailyBytes = doc.DailyBytes, MonthlyBytes = doc.MonthlyBytes, Exempt = doc.Exempt,
        Action = doc.Action, ThrottleKbps = doc.ThrottleKbps,
        WarnPercents = doc.WarnPercents, DryRun = doc.DryRun
    };

    private static QuotaPolicyDocument ToDocument(QuotaPolicy policy) => new()
    {
        Id = policy.Id, Scope = policy.Scope, CategoryKey = policy.CategoryKey,
        Mac = policy.Mac?.ToString(), DailyBytes = policy.DailyBytes,
        MonthlyBytes = policy.MonthlyBytes, Exempt = policy.Exempt, Action = policy.Action,
        ThrottleKbps = policy.ThrottleKbps, WarnPercents = policy.WarnPercents, DryRun = policy.DryRun
    };
}
