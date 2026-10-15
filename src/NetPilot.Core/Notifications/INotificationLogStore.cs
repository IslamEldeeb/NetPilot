namespace NetPilot.Core.Notifications;

public enum NotificationOutcome { Sent, SuppressedQuietHours, SuppressedDuplicate, Failed }

public record NotificationLogEntry(DateTimeOffset AtUtc, string Tag, string Title, string ChannelId,
    NotificationOutcome Outcome, string? Error = null);

public interface INotificationLogStore
{
    Task AppendAsync(NotificationLogEntry entry, CancellationToken ct);
    Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken ct);
    Task PruneAsync(int keepNewest, CancellationToken ct);
}
