namespace NetPilot.Core.Usage;

public record LocalTrafficStatus
{
    public bool IsConfigured { get; init; }
    public DateTimeOffset? LastReadAtUtc { get; init; }
    public DateTimeOffset? GeneratedAtUtc { get; init; }
    public DateTimeOffset? UnavailableSinceUtc { get; init; }
    public DateTimeOffset? LastWarningAtUtc { get; init; }
}

public interface ILocalTrafficStore
{
    Task<IReadOnlyList<LanFlowCounter>> GetFlowsAsync(CancellationToken ct);
    Task SaveFlowsAsync(IReadOnlyList<LanFlowCounter> flows, CancellationToken ct);
    Task<LocalTrafficStatus> GetStatusAsync(CancellationToken ct);
    /// <summary>Atomically records an outage and claims its hourly warning across processes.</summary>
    Task<bool> TryRecordFailureAsync(DateTimeOffset now, CancellationToken ct);
    Task SaveStatusAsync(LocalTrafficStatus status, CancellationToken ct);
}
