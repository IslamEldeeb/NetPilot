using JsonSerializer = System.Text.Json.JsonSerializer;
using LiteDB;
using NetPilot.Core.Automations;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;

namespace NetPilot.Data;

// LiteDB transactions are thread-bound; these store operations complete synchronously inside the transaction.
public sealed class LiteAutomationCommands(NetPilotDatabase db, IRuleStore rules, IQuotaStore quotas,
    IActivityLogStore activity, UsagePeriodClock clock) : IAutomationCommands
{
    private readonly ILiteCollection<AutomationSnapshot> _snapshots = db.GetCollection<AutomationSnapshot>("automation_changes");

    public Task<AutomationChange> SaveQuotaAsync(QuotaPolicy? before, QuotaPolicy? after, CancellationToken ct)
    {
        if (after?.Validate() is { Count: > 0 } errors) throw new ArgumentException(string.Join(' ', errors));
        if (before is null && after is null || before is not null && after is not null && before.Id != after.Id)
            throw new ArgumentException("Quota IDs must match.");
        return SaveAsync("quota:" + (before?.Id ?? after!.Id), Serialize(before), Serialize(after), ct);
    }

    public Task<AutomationChange> SaveRuleAsync(Rule? before, Rule? after, CancellationToken ct)
    {
        if (after?.Validate() is { Count: > 0 } errors) throw new ArgumentException(string.Join(' ', errors));
        if (before is null && after is null || before is not null && after is not null && before.Id != after.Id)
            throw new ArgumentException("Rule IDs must match.");
        return SaveAsync("rule:" + (before?.Id ?? after!.Id), Serialize(before), Serialize(after), ct);
    }

    private Task<AutomationChange> SaveAsync(string target, string? expected, string? replacement, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new AutomationSnapshot
        {
            Id = Guid.NewGuid().ToString(), Target = target, Before = expected, After = replacement,
            UndoUntilUtc = clock.GetUtcNow().AddSeconds(10)
        };
        db.InTransaction(() =>
        {
            if (Current(target) != expected) throw new InvalidOperationException("This configuration changed. Refresh before saving.");
            Write(target, replacement, ct);
            snapshot.Revision = AutomationRevision.Current(db, target);
            _snapshots.Insert(snapshot);
            AppendAudit(snapshot, ActivityEventType.ConfigurationChanged, ct);
            _snapshots.DeleteMany(s => s.UndoUntilUtc < clock.GetUtcNow().AddDays(-62));
        });
        return Task.FromResult(new AutomationChange(snapshot.Id, snapshot.UndoUntilUtc));
    }

    public Task<UndoOutcome> UndoAsync(string changeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var outcome = UndoOutcome.NotFound;
        db.InTransaction(() =>
        {
            var snapshot = _snapshots.FindById(changeId);
            if (snapshot is null) return;
            if (snapshot.Restored) { outcome = UndoOutcome.AlreadyRestored; return; }
            if (clock.GetUtcNow() >= snapshot.UndoUntilUtc) { outcome = UndoOutcome.Expired; return; }
            if (AutomationRevision.Current(db, snapshot.Target) != snapshot.Revision || Current(snapshot.Target) != snapshot.After)
            { outcome = UndoOutcome.Conflict; return; }
            Write(snapshot.Target, snapshot.Before, ct);
            snapshot.Restored = true;
            _snapshots.Update(snapshot);
            AppendAudit(snapshot, ActivityEventType.ConfigurationRestored, ct);
            outcome = UndoOutcome.Restored;
        });
        return Task.FromResult(outcome);
    }

    public Task ImportRulesAsync(IReadOnlyList<Rule> imported, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        db.InTransaction(() =>
        {
            var validated = RulePortability.Import(RulePortability.Export(imported), rules.GetAllAsync(ct).GetAwaiter().GetResult().Select(r => r.Id));
            foreach (var rule in validated)
                SaveRuleAsync(null, rule with { CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() }, ct).GetAwaiter().GetResult();
        });
        return Task.CompletedTask;
    }

    private string? Current(string target) => target.StartsWith("rule:", StringComparison.Ordinal)
        ? Serialize(rules.FindAsync(Guid.Parse(target[5..]), CancellationToken.None).GetAwaiter().GetResult())
        : Serialize(quotas.FindAsync(target[6..], CancellationToken.None).GetAwaiter().GetResult());

    private void Write(string target, string? payload, CancellationToken ct)
    {
        if (target.StartsWith("rule:", StringComparison.Ordinal))
        {
            if (payload is null) rules.DeleteAsync(Guid.Parse(target[5..]), ct).GetAwaiter().GetResult();
            else rules.UpsertAsync(JsonSerializer.Deserialize<Rule>(payload, RuleSerialization.Options)!, ct).GetAwaiter().GetResult();
        }
        else
        {
            if (payload is null) quotas.DeleteAsync(target[6..], ct).GetAwaiter().GetResult();
            else quotas.UpsertAsync(JsonSerializer.Deserialize<QuotaPolicy>(payload, RuleSerialization.Options)!, ct).GetAwaiter().GetResult();
        }
    }

    private void AppendAudit(AutomationSnapshot snapshot, ActivityEventType type, CancellationToken ct) =>
        activity.AppendAsync(new(clock.GetUtcNow(), type, null,
            $"{(type == ActivityEventType.ConfigurationRestored ? "Restored" : "Saved")} {snapshot.Target}. Awaiting the Agent's next tick.")
        {
            Source = "automations", Actor = "Administrator", CorrelationId = snapshot.Target.StartsWith("rule:", StringComparison.Ordinal) ? snapshot.Target[5..] : snapshot.Target, ChangeId = snapshot.Id,
            Kind = snapshot.Target.StartsWith("rule:", StringComparison.Ordinal) ? "rule" : "quota"
        }, ct).GetAwaiter().GetResult();

    private static string? Serialize<T>(T? configuration) => configuration is null ? null
        : JsonSerializer.Serialize(configuration, RuleSerialization.Options);
}

public sealed class AutomationSnapshot
{
    [BsonId] public string Id { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Before { get; set; }
    public string? After { get; set; }
    public string Revision { get; set; } = "";
    public DateTimeOffset UndoUntilUtc { get; set; }
    public bool Restored { get; set; }
}
