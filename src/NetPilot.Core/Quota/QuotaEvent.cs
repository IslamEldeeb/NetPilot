using NetPilot.Core.Devices;

namespace NetPilot.Core.Quota;

public enum QuotaEventKind { Warning, Exceeded, DryRun, ActionApplied, Reverted, ActionSkippedProtected }

public record QuotaEvent(
    QuotaEventKind Kind,
    MacAddress Mac,
    string DeviceDisplayName,
    long UsedBytes,
    decimal CapBytes,
    decimal Percent,
    QuotaPeriodKind PeriodKind,
    string PeriodKey,
    QuotaAction Action,
    bool DryRun,
    DateTimeOffset AtUtc,
    int? ThrottleKbps = null,
    int? WarnPercent = null);

public interface IQuotaEventSink
{
    Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct);
}

public sealed class NullQuotaEventSink : IQuotaEventSink
{
    public Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct) => Task.CompletedTask;
}
