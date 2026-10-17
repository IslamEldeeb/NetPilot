using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetPilot.Core.Devices;
using NetPilot.Core.Health;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Safety;

public enum SafetyPolicyKind { Quota, Rule }
public sealed record SafetyEvidence(string Id, string Actor, string Method, DateTimeOffset AtUtc);
public sealed record PolicyReview(SafetyPolicyKind Kind, string Id, string Name, string Fingerprint, bool DryRun, string Action, int Targets)
{
    public string[] TargetMacs { get; init; } = [];
    public string RehearsalId => $"release:{Kind}:{Id}:{Fingerprint}";
}
public sealed record GraduationRecord(string Id, PolicyReview Policy, string Actor, DateTimeOffset AtUtc, SafetyEvidence[] Evidence);
public interface ISafetyStore
{
    Task<IReadOnlyList<SafetyEvidence>> GetEvidenceAsync(CancellationToken ct);
    Task SaveEvidenceAsync(SafetyEvidence evidence, CancellationToken ct);
    Task<IReadOnlyList<GraduationRecord>> GetGraduationsAsync(CancellationToken ct);
    // Compare the reviewed fingerprint and save the policy + record in one transaction.
    Task<bool> TryGraduateAsync(GraduationRecord record, CancellationToken ct);
}

public static class PolicyFingerprint
{
    public static string For(QuotaPolicy policy) => Hash(new { Policy = policy with { Mac = null }, Mac = policy.Mac?.ToString() });
    public static string For(Rule rule) => Hash(new { Policy = rule with { Scope = rule.Scope with { Devices = [] } }, Devices = rule.Scope.Devices.Select(m => m.ToString()).ToArray() });
    private static string Hash<T>(T policy) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(policy))));
}

public sealed class SafetyGraduationService(ISafetyStore safety, IQuotaStore quotas, IRuleStore rules,
    IDeviceStore devices, IAgentHeartbeatStore heartbeats, UsagePeriodClock clock, RulesOptions options)
{
    public static readonly string[] RequiredEvidence = ["counters", "lan-exclusion", "coverage", "protection", "windows", "phone"];

    public async Task<IReadOnlyList<PolicyReview>> GetPoliciesAsync(CancellationToken ct)
    {
        var allDevices = await devices.GetAllAsync(ct);
        var allQuotas = await quotas.GetAllAsync(ct);
        var reviews = allQuotas.Select(p => new PolicyReview(SafetyPolicyKind.Quota, p.Id, p.Id,
            PolicyFingerprint.For(p), p.DryRun, p.Action.ToString(),
            allDevices.Count(d => QuotaResolver.Resolve(d, allQuotas).MatchedPolicy?.Id == p.Id))
            { TargetMacs = allDevices.Where(d => QuotaResolver.Resolve(d, allQuotas).MatchedPolicy?.Id == p.Id).Select(d => d.Mac.ToString()).Order().ToArray() }).ToList();
        reviews.AddRange((await rules.GetAllAsync(ct)).Select(r => new PolicyReview(SafetyPolicyKind.Rule, r.Id.ToString(), r.Name,
            PolicyFingerprint.For(r), r.DryRun, string.Join(", ", r.Actions.Select(a => a.GetType().Name)), allDevices.Count(r.Scope.Matches)) { TargetMacs = allDevices.Where(r.Scope.Matches).Select(d => d.Mac.ToString()).Order().ToArray() }));
        return reviews;
    }

    public async Task<string?> BlockReasonAsync(SafetyPolicyKind kind, CancellationToken ct)
    {
        var heartbeat = await heartbeats.GetAsync(ct);
        return SafetyGate.BlockReason(kind, heartbeat, clock.GetUtcNow(), options);
    }

    public async Task<string?> GraduateAsync(PolicyReview reviewed, string actor, bool confirmed, CancellationToken ct)
    {
        if (!confirmed) return "Explicit confirmation is required.";
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 80) return "Enter the administrator name (up to 80 characters).";
        if (await BlockReasonAsync(reviewed.Kind, ct) is { } blocked) return blocked;
        var current = (await GetPoliciesAsync(ct)).FirstOrDefault(p => p.Kind == reviewed.Kind && p.Id == reviewed.Id);
        if (current is null || current.Fingerprint != reviewed.Fingerprint || !current.TargetMacs.SequenceEqual(reviewed.TargetMacs))
            return "The policy or target count changed. Reload and review it again.";
        if (!current.DryRun) return "This policy is already configured for enforcement.";
        var evidence = await safety.GetEvidenceAsync(ct);
        var required = RequiredEvidence.Append(current.RehearsalId).ToArray();
        var accepted = evidence.Where(e => required.Contains(e.Id) && !string.IsNullOrWhiteSpace(e.Method)
            && !string.IsNullOrWhiteSpace(e.Actor) && e.AtUtc <= clock.GetUtcNow()).ToArray();
        if (required.Any(id => accepted.All(e => e.Id != id))) return "Complete every evidence item and the release rehearsal for this policy.";
        var record = new GraduationRecord(Guid.NewGuid().ToString(), current, actor.Trim(), clock.GetUtcNow(), accepted);
        return await safety.TryGraduateAsync(record, ct) ? null : "The policy changed. Reload and review it again.";
    }
}

public static class SafetyGate
{
    public static string? BlockReason(SafetyPolicyKind kind, AgentHeartbeat? heartbeat, DateTimeOffset now, RulesOptions options)
    {
        if (AgentHealth.Evaluate(heartbeat, now) != AgentHealthStatus.Healthy || heartbeat!.ConsecutiveFailedTicks != 0
            || heartbeat.LastErrorSummary is not null || heartbeat.LastSuccessfulRouterReadUtc is not { } read || read > now
            || now - read > TimeSpan.FromSeconds(2d * heartbeat.TickIntervalSeconds + 60))
            return "A fresh successful Agent tick and router read are required. Retry after the Agent recovers.";
        if (kind == SafetyPolicyKind.Rule && (options.ForceDryRun || heartbeat.EffectiveModes.RulesForceDryRun))
            return "Rules are forced dry-run. Deployment configuration must be reviewed outside this UI; wait for the Agent to report the change.";
        if (kind == SafetyPolicyKind.Rule && (!options.Enabled || !heartbeat.EffectiveModes.RulesEnabled))
            return "The rules kill switch is off in Web or Agent configuration.";
        return null;
    }
}
