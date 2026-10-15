using NetPilot.Core.Notifications;

namespace NetPilot.Notifications.WebPush;

public class NotificationService(WebPushChannel channel)
{
    // Direct delivery deliberately bypasses dispatcher quiet hours and duplicate suppression.
    public Task<IReadOnlyList<PushDeliveryResult>> SendTestAsync(string? label = null, CancellationToken ct = default) =>
        channel.SendToSubscriptionsAsync(new("NetPilot test", label is null ? "Notifications are working." : $"Notifications are working: {label}",
            "/", "netpilot:test", []), ct);
}
