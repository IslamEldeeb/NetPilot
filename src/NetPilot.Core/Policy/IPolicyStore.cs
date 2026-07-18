namespace NetPilot.Core.Policy;

public interface IPolicyStore
{
    Task<DevicePolicy?> FindByCategoryAsync(string categoryKey, CancellationToken ct);
    Task<IReadOnlyList<DevicePolicy>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(DevicePolicy policy, CancellationToken ct);

    /// <summary>No-ops if the category already has a policy row.</summary>
    Task EnsureSeedCategoriesAsync(CancellationToken ct);
}
