using JsonSerializer = System.Text.Json.JsonSerializer;
using LiteDB;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Safety;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public sealed class LiteSafetyStore(NetPilotDatabase db) : ISafetyStore
{
    private readonly ILiteCollection<SafetyDocument> _evidence = db.GetCollection<SafetyDocument>("safety_evidence");
    private readonly ILiteCollection<SafetyDocument> _graduations = db.GetCollection<SafetyDocument>("safety_graduations");
    public Task<IReadOnlyList<SafetyEvidence>> GetEvidenceAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SafetyEvidence>>(_evidence.FindAll().Select(d => JsonSerializer.Deserialize<SafetyEvidence>(d.Payload)!).ToArray());
    public Task<IReadOnlyList<GraduationRecord>> GetGraduationsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GraduationRecord>>(_graduations.FindAll().Select(d => JsonSerializer.Deserialize<GraduationRecord>(d.Payload)!).OrderByDescending(g => g.AtUtc).ToArray());
    public Task SaveEvidenceAsync(SafetyEvidence evidence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(evidence.Id) || string.IsNullOrWhiteSpace(evidence.Actor) || evidence.Actor.Length > 80
            || string.IsNullOrWhiteSpace(evidence.Method) || evidence.Method.Length > 2000)
            throw new ArgumentException("Evidence needs an administrator and a method (up to 2000 characters).");
        _evidence.Upsert(new SafetyDocument { Id = evidence.Id, Payload = JsonSerializer.Serialize(evidence) });
        return Task.CompletedTask;
    }
    public Task<bool> TryGraduateAsync(GraduationRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var saved = false;
        db.InTransaction(() =>
        {
            if (record.Policy.Kind == SafetyPolicyKind.Quota)
            {
                var store = new LiteQuotaStore(db);
                var policy = store.FindAsync(record.Policy.Id, ct).GetAwaiter().GetResult();
                if (policy is null || !policy.DryRun || Core.Safety.PolicyFingerprint.For(policy) != record.Policy.Fingerprint) return;
                store.UpsertAsync(policy with { DryRun = false }, ct).GetAwaiter().GetResult();
            }
            else
            {
                var collection = db.GetCollection<RuleDocument>("rules");
                var doc = collection.FindById(record.Policy.Id);
                if (doc is null) return;
                var rule = JsonSerializer.Deserialize<Rule>(doc.Payload, RuleJson.Options)!;
                if (!rule.DryRun || Core.Safety.PolicyFingerprint.For(rule) != record.Policy.Fingerprint) return;
                collection.Upsert(new RuleDocument { Id = doc.Id, Payload = JsonSerializer.Serialize(rule with { DryRun = false, UpdatedAtUtc = record.AtUtc }, RuleJson.Options) });
            }
            _graduations.Insert(new SafetyDocument { Id = record.Id, Payload = JsonSerializer.Serialize(record) });
            saved = true;
        });
        return Task.FromResult(saved);
    }
    public sealed class SafetyDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
