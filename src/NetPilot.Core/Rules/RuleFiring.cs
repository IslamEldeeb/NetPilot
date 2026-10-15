using NetPilot.Core.Devices;

namespace NetPilot.Core.Rules;

public enum RuleFiringOutcome { Applied, DryRun, SkippedProtected, Failed, SuppressedCooldown }
public enum RuleFiringStatus { Active, Reverted, Released }
public record WirelessPrior(string NetworkId, bool PriorEnabled, bool AppliedEnabled);

public record RuleFiring
{
    public string Id => $"{RuleId}|{Subject}|{EpisodeKey}";
    public Guid RuleId { get; init; }
    public string RuleName { get; init; } = "";
    public string Subject { get; init; } = "";
    public string EpisodeKey { get; init; } = "";
    public DateTimeOffset FiredAtUtc { get; init; }
    public bool DryRun { get; init; }
    public RuleFiringOutcome Outcome { get; init; }
    public DateTimeOffset? RevertAtUtc { get; init; }
    public bool RevertWhenTriggerEnds { get; init; }
    public RuleFiringStatus Status { get; init; } = RuleFiringStatus.Active;
    public string[] AppliedClaimIds { get; init; } = [];
    public WirelessPrior[] WirelessPrior { get; init; } = [];
    public bool ReleaseRequested { get; init; }
    public string? Error { get; init; }

    // Capture intent before effects so edits, deletion and an interrupted tick remain recoverable.
    public RuleAction[] Actions { get; init; } = [];
    public MacAddress[] TargetMacs { get; init; } = [];
    public string[] CompletedActionKeys { get; init; } = [];
    public bool SkippedProtected { get; init; }
    public bool Completed { get; init; }
    public bool Reverting { get; init; }
    public TimeWindow? Window { get; init; }
    public RulePeriod? RevertPeriod { get; init; }
    public string? RevertPeriodKey { get; init; }
    public string TriggerEpisodeKey { get; init; } = "";
    public string DayKey { get; init; } = "";
    public string MonthKey { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public decimal UsedBytes { get; init; }
    public long CapBytes { get; init; }
}

public interface IRuleStore
{
    Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken ct);
    Task<Rule?> FindAsync(Guid id, CancellationToken ct);
    Task UpsertAsync(Rule rule, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IRuleFiringStore
{
    Task<IReadOnlyList<RuleFiring>> GetAllAsync(CancellationToken ct);
    Task<RuleFiring?> FindAsync(string id, CancellationToken ct);
    Task<RuleFiring?> FindLatestAsync(Guid ruleId, string subject, CancellationToken ct);
    Task<RuleFiring> UpdateAsync(string id, Func<RuleFiring?, RuleFiring> update, CancellationToken ct);
    // Returns false if missing or no longer active; repeated active requests succeed.
    Task<bool> RequestReleaseAsync(string id, CancellationToken ct);
    Task PruneAsync(int keepNewest, CancellationToken ct);
}

public class RulesOptions
{
    public bool Enabled { get; set; } = true;
    public bool ForceDryRun { get; set; }
    public int MaxRouterActionsPerTick { get; set; } = 10;

    public void Validate()
    {
        if (MaxRouterActionsPerTick < 0) throw new ArgumentException("NetPilot:Rules:MaxRouterActionsPerTick must be zero or more.");
    }
}
