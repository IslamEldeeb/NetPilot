using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using LiteDB;
using NetPilot.Core.Rules;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteRuleFiringStore : IRuleFiringStore
{
    private readonly NetPilotDatabase _db;
    private readonly ILiteCollection<RuleFiringDocument> _firings;
    private readonly ILiteCollection<RuleFiringDocument> _latest;

    public LiteRuleFiringStore(NetPilotDatabase db)
    {
        _db = db;
        _firings = db.GetCollection<RuleFiringDocument>("rule_firings");
        _firings.EnsureIndex(f => f.FiredAtTicks);
        _firings.EnsureIndex(f => f.Status);
        // History is bounded, but pruning must never make DeviceJoined/idle/once-per fire again.
        _latest = db.GetCollection<RuleFiringDocument>("rule_firing_latest");
    }

    public Task<IReadOnlyList<RuleFiring>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RuleFiring>>(_firings.FindAll().Select(Read).ToArray());

    public Task<RuleFiring?> FindAsync(string id, CancellationToken ct)
    {
        var doc = _firings.FindById(id);
        return Task.FromResult(doc is null ? null : Read(doc));
    }

    public Task<RuleFiring?> FindLatestAsync(Guid ruleId, string subject, CancellationToken ct)
    {
        var doc = _latest.FindById($"{ruleId}|{subject}");
        return Task.FromResult(doc is null ? null : Read(doc));
    }

    public Task<RuleFiring> UpdateAsync(string id, Func<RuleFiring?, RuleFiring> update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RuleFiring updated = null!;
        _db.InTransaction(() =>
        {
            var current = _firings.FindById(id);
            updated = update(current is null ? null : Read(current));
            if (updated.Id != id) throw new ArgumentException("Rule firing identity cannot change.");
            var doc = Document(updated);
            _firings.Upsert(doc);
            var latestId = $"{updated.RuleId}|{updated.Subject}";
            var latest = _latest.FindById(latestId);
            if (latest is not null && latest.FiredAtTicks > doc.FiredAtTicks) return;
            doc.Id = latestId;
            _latest.Upsert(doc);
        });
        return Task.FromResult(updated);
    }

    public Task PruneAsync(int keepNewest, CancellationToken ct)
    {
        if (keepNewest < 0) throw new ArgumentOutOfRangeException(nameof(keepNewest));
        _db.InTransaction(() =>
        {
            var expired = _firings.FindAll().OrderByDescending(f => f.FiredAtTicks).Skip(keepNewest)
                .Where(f => f.Status != RuleFiringStatus.Active).Select(f => f.Id).ToArray();
            foreach (var id in expired) _firings.Delete(id);
        });
        return Task.CompletedTask;
    }

    private static RuleFiring Read(RuleFiringDocument doc) =>
        JsonSerializer.Deserialize<RuleFiring>(doc.Payload, RuleJson.Options) ?? throw new JsonException("Missing firing payload.");

    private static RuleFiringDocument Document(RuleFiring firing) => new()
    {
        Id = firing.Id, RuleId = firing.RuleId.ToString(), Subject = firing.Subject, FiredAtTicks = firing.FiredAtUtc.UtcTicks,
        Status = firing.Status, Payload = JsonSerializer.Serialize(firing, RuleJson.Options)
    };
}
