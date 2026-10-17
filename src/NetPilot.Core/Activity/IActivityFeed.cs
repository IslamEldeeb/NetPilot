using NetPilot.Core.Enforcement;

namespace NetPilot.Core.Activity;

public record ActivityFilter
{
    public string Kind { get; init; } = "all";
    public string? Mac { get; init; }
    public bool? DryRun { get; init; }
    public bool FailedOnly { get; init; }
    public bool Inbox { get; init; }
    public int? BeforeId { get; init; }
    public int PageSize { get; init; } = 30;
}

public record ActivityPage(IReadOnlyList<ActivityLogEntry> Entries, int? NextId);

public interface IActivityFeed
{
    Task<ActivityPage> QueryAsync(ActivityFilter filter, CancellationToken ct);
    Task<ActivityLogEntry?> FindAsync(int eventId, CancellationToken ct);
    Task<int> UnreadCountAsync(CancellationToken ct);
    Task MarkReadAsync(int eventId, CancellationToken ct);
    Task PruneAsync(DateTimeOffset beforeUtc, CancellationToken ct);
}
