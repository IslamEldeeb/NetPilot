using System.Globalization;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Notifications;

public class NotificationQuotaEventSink(NotificationDispatcher dispatcher, IQuotaActionTokenService tokens) : IQuotaEventSink
{
    public Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct) => dispatcher.DispatchAsync(
        ToMessage(quotaEvent, tokens), quotaEvent.Kind is QuotaEventKind.Warning or QuotaEventKind.DryRun
            ? NotificationInterruption.QuietHoursEligible : NotificationInterruption.Immediate, ct);

    public static NotificationMessage ToMessage(QuotaEvent e, IQuotaActionTokenService tokens)
    {
        var period = e.PeriodKind == QuotaPeriodKind.Day ? "daily" : "monthly";
        var body = e.Kind switch
        {
            QuotaEventKind.Warning => $"{e.DeviceDisplayName}: {Bytes(e.UsedBytes)} of {Bytes(e.CapBytes)} used {(e.PeriodKind == QuotaPeriodKind.Day ? "today" : "this month")} ({e.Percent.ToString("0.#", CultureInfo.InvariantCulture)}%)",
            QuotaEventKind.Exceeded => $"{e.DeviceDisplayName} reached its {period} limit",
            QuotaEventKind.ActionApplied => e.Action switch
            {
                QuotaAction.Throttle => $"{e.DeviceDisplayName} throttled to {e.ThrottleKbps} Kbps",
                QuotaAction.Block => $"{e.DeviceDisplayName} blocked",
                _ => $"{e.DeviceDisplayName} quota notification applied"
            },
            QuotaEventKind.Reverted => $"{e.DeviceDisplayName} restored",
            QuotaEventKind.DryRun => DryRunBody(e),
            QuotaEventKind.ActionSkippedProtected => $"{e.DeviceDisplayName} is over quota but protected - no action taken",
            _ => throw new ArgumentOutOfRangeException(nameof(e))
        };
        var tag = $"quota:{e.Mac}:{e.PeriodKind}:{e.PeriodKey}:{e.Kind}";
        if (e.Kind == QuotaEventKind.Warning) tag += $":{e.WarnPercent}";
        var actions = !e.DryRun && e.Kind is QuotaEventKind.Exceeded or QuotaEventKind.ActionApplied
            ? new[]
            {
                new NotificationAction(NotificationActionIds.Extend1Gb, e.PeriodKind == QuotaPeriodKind.Day ? "+1 GB today" : "+1 GB this month",
                    tokens.Create(new(e.Mac, e.PeriodKind, e.PeriodKey, NotificationActionIds.Extend1Gb))),
                new NotificationAction(NotificationActionIds.RestoreNow, "Restore now",
                    tokens.Create(new(e.Mac, e.PeriodKind, e.PeriodKey, NotificationActionIds.RestoreNow)))
            } : [];
        return new("NetPilot quota", body, "/", tag, actions) { Event = e.Kind switch
        {
            QuotaEventKind.Warning => Settings.NotificationEvent.QuotaWarning,
            QuotaEventKind.Exceeded => Settings.NotificationEvent.QuotaExceeded,
            QuotaEventKind.ActionApplied => Settings.NotificationEvent.QuotaApplied,
            QuotaEventKind.Reverted => Settings.NotificationEvent.QuotaReverted,
            QuotaEventKind.DryRun => Settings.NotificationEvent.QuotaDryRun,
            _ => Settings.NotificationEvent.QuotaProtected
        } };
    }

    private static string DryRunBody(QuotaEvent e) => e.Action switch
    {
        QuotaAction.Throttle => $"[Dry-run] would have throttled {e.DeviceDisplayName} to {e.ThrottleKbps} Kbps",
        QuotaAction.Block => $"[Dry-run] would have blocked {e.DeviceDisplayName}",
        _ => $"[Dry-run] would have notified for {e.DeviceDisplayName}"
    };

    private static string Bytes(decimal bytes) => bytes >= 1_000_000_000m
        ? $"{(bytes / 1_000_000_000m).ToString("0.###", CultureInfo.InvariantCulture)} GB"
        : $"{(bytes / 1_000_000m).ToString("0.###", CultureInfo.InvariantCulture)} MB";
}
