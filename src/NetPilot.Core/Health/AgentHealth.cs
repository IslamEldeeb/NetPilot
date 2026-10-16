namespace NetPilot.Core.Health;

public enum AgentHealthStatus { Unknown, Healthy, Stale, Down }

public static class AgentHealth
{
    // Boundaries are strict (>); future timestamps cannot prove freshness across clock skew.
    public static AgentHealthStatus Evaluate(AgentHeartbeat? heartbeat, DateTimeOffset nowUtc, int graceSeconds = 60)
    {
        if (heartbeat is null || heartbeat.TickIntervalSeconds <= 0 || graceSeconds < 0 ||
            heartbeat.LastTickStartedUtc > nowUtc || heartbeat.LastTickCompletedUtc > nowUtc)
            return AgentHealthStatus.Unknown;
        var age = (nowUtc - (heartbeat.LastTickCompletedUtc ?? heartbeat.LastTickStartedUtc)).TotalSeconds;
        if (age > 5d * heartbeat.TickIntervalSeconds + graceSeconds) return AgentHealthStatus.Down;
        if (age > 2d * heartbeat.TickIntervalSeconds + graceSeconds) return AgentHealthStatus.Stale;
        return heartbeat.LastTickCompletedUtc is null ? AgentHealthStatus.Unknown : AgentHealthStatus.Healthy;
    }
}

public enum ApplyState { Saved, Pending, Applied, AgentDown }

public static class ApplyStatus
{
    /// <summary>Tick-level evidence only, not acknowledgement of any individual router action.</summary>
    public static ApplyState For(DateTimeOffset savedAtUtc, AgentHeartbeat? heartbeat, DateTimeOffset nowUtc)
    {
        var health = AgentHealth.Evaluate(heartbeat, nowUtc);
        if (health == AgentHealthStatus.Unknown) return ApplyState.Saved;
        var completedAfterSave = heartbeat!.LastTickCompletedUtc >= savedAtUtc && savedAtUtc <= nowUtc;
        if (completedAfterSave && heartbeat.ConsecutiveFailedTicks == 0 && heartbeat.LastErrorSummary is null)
            return ApplyState.Applied;
        return health is AgentHealthStatus.Stale or AgentHealthStatus.Down ? ApplyState.AgentDown : ApplyState.Pending;
    }
}
