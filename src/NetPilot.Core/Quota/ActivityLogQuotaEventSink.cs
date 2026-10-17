using NetPilot.Core.Enforcement;

namespace NetPilot.Core.Quota;

public class ActivityLogQuotaEventSink(IActivityLogStore activityLog) : IQuotaEventSink
{
    public Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct)
    {
        var (type, description) = quotaEvent.Kind switch
        {
            QuotaEventKind.Warning => (ActivityEventType.QuotaWarning, $"Quota warning ({quotaEvent.WarnPercent}% threshold)"),
            QuotaEventKind.Exceeded => (ActivityEventType.QuotaExceeded, "Quota exceeded"),
            QuotaEventKind.DryRun => (ActivityEventType.QuotaDryRun, DescribeDryRun(quotaEvent)),
            QuotaEventKind.ActionApplied => (ActivityEventType.QuotaActionApplied, $"Quota action applied: {quotaEvent.Action}"),
            QuotaEventKind.Reverted => (ActivityEventType.QuotaReverted, $"Quota action reverted: {quotaEvent.Action}"),
            QuotaEventKind.ActionSkippedProtected => (ActivityEventType.QuotaActionSkippedProtected,
                $"Quota {quotaEvent.Action} action skipped because the device is protected"),
            _ => throw new ArgumentOutOfRangeException(nameof(quotaEvent))
        };
        return activityLog.AppendAsync(new(quotaEvent.AtUtc, type, quotaEvent.Mac,
            $"{description} — {quotaEvent.DeviceDisplayName}: {quotaEvent.UsedBytes} / {quotaEvent.CapBytes} bytes "
            + $"({quotaEvent.Percent:F1}%), {quotaEvent.PeriodKind} {quotaEvent.PeriodKey}")
        { Source = "quotas", Actor = "Agent", Kind = "quota", DryRun = quotaEvent.DryRun, CorrelationId = $"quota:{quotaEvent.Mac}:{quotaEvent.PeriodKind}:{quotaEvent.PeriodKey}" }, ct);
    }

    private static string DescribeDryRun(QuotaEvent quotaEvent) => quotaEvent.Action switch
    {
        QuotaAction.Throttle => $"Dry run: would have throttled {quotaEvent.DeviceDisplayName} to {quotaEvent.ThrottleKbps} Kbps",
        QuotaAction.Block => $"Dry run: would have blocked {quotaEvent.DeviceDisplayName}",
        _ => $"Dry run: would have notified for {quotaEvent.DeviceDisplayName}"
    };
}
