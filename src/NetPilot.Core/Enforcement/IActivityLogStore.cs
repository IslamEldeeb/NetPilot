namespace NetPilot.Core.Enforcement;

public interface IActivityLogStore
{
    Task AppendAsync(ActivityLogEntry entry, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int count, CancellationToken ct);
}
