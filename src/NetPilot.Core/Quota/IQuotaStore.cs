namespace NetPilot.Core.Quota;

public interface IQuotaStore
{
    Task<IReadOnlyList<QuotaPolicy>> GetAllAsync(CancellationToken ct);
    Task<QuotaPolicy?> FindAsync(string id, CancellationToken ct);
    Task UpsertAsync(QuotaPolicy policy, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
}
