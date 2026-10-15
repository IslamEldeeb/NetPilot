namespace NetPilot.Core.Quota;

public interface IQuotaStateStore
{
    Task<QuotaPeriodState?> FindAsync(string id, CancellationToken ct);
    Task UpsertAsync(QuotaPeriodState state, CancellationToken ct);
    Task<IReadOnlyList<QuotaPeriodState>> ListWithActiveActionAsync(CancellationToken ct);

    /// <summary>Active actions are retained regardless of age so their reversal tokens cannot be lost.</summary>
    Task PruneAsync(DateTimeOffset olderThanUtc, CancellationToken ct);
}
