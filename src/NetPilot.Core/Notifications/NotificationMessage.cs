namespace NetPilot.Core.Notifications;

public record NotificationAction(string Id, string Title);

public record NotificationMessage(string Title, string Body, string? Url, string Tag, NotificationAction[] Actions);

public static class NotificationActionIds
{
    public const string Extend1Gb = "extend-1gb";
    public const string RestoreNow = "restore-now";
}

public interface INotificationChannel
{
    string Id { get; }
    Task SendAsync(NotificationMessage message, CancellationToken ct);
}
