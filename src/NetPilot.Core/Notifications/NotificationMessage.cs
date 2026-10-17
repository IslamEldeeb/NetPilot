namespace NetPilot.Core.Notifications;

public record NotificationAction(string Id, string Title, string Token);

public record NotificationMessage(string Title, string Body, string? Url, string Tag, NotificationAction[] Actions)
{
    public bool? DryRun { get; init; }
    public string? CorrelationId { get; init; }
    public NetPilot.Core.Devices.MacAddress? Mac { get; init; }
    public NetPilot.Core.Settings.NotificationEvent Event { get; init; }
}

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
