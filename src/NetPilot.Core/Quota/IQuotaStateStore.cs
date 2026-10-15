namespace NetPilot.Core.Quota;

public interface IQuotaStateStore
{
    Task<QuotaPeriodState?> FindAsync(string id, CancellationToken ct);
    /// <summary>Runs the synchronous mutation inside a store transaction; null leaves the row unchanged. Do not perform I/O in the callback.</summary>
    Task<QuotaPeriodState?> UpdateAsync(string id, Func<QuotaPeriodState?, QuotaPeriodState?> update, CancellationToken ct);
    Task UpsertAsync(QuotaPeriodState state, CancellationToken ct);
    Task<IReadOnlyList<QuotaPeriodState>> ListWithActiveActionAsync(CancellationToken ct);

    /// <summary>Active actions are retained regardless of age so their reversal tokens cannot be lost.</summary>
    Task PruneAsync(DateTimeOffset olderThanUtc, CancellationToken ct);
}
