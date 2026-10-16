namespace NetPilot.Core.Health;

public sealed record AgentHeartbeat
{
    public DateTimeOffset LastTickStartedUtc { get; init; }
    public DateTimeOffset? LastTickCompletedUtc { get; init; }
    public double? LastTickDurationMs { get; init; }
    public DateTimeOffset? LastSuccessfulRouterReadUtc { get; init; }
    public int ConsecutiveFailedTicks { get; init; }
    public string? LastErrorSummary { get; init; }
    public int TickIntervalSeconds { get; init; }
    public string AppVersion { get; init; } = "";
    public AgentEffectiveModes EffectiveModes { get; init; } = new(false, false);
}

public sealed record AgentEffectiveModes(bool RulesForceDryRun, bool RulesEnabled);

public interface IAgentHeartbeatStore
{
    Task<AgentHeartbeat?> GetAsync(CancellationToken ct);
    Task SaveAsync(AgentHeartbeat heartbeat, CancellationToken ct);
}
