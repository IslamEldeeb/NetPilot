using NetPilot.Core.Devices;

namespace NetPilot.Core.Enforcement;

public record ActivityLogEntry(DateTimeOffset AtUtc, ActivityEventType Type, MacAddress? Mac, string Message)
{
    public int EventId { get; init; }
    public string Source { get; init; } = "system";
    public string Actor { get; init; } = "Unknown";
    public string? CorrelationId { get; init; }
    public string? DeduplicationKey { get; init; }
    public string Kind { get; init; } = "system";
    public bool? DryRun { get; init; }
    public bool Failed { get; init; }
    public bool IsNotification { get; init; }
    public DateTimeOffset? ReadAtUtc { get; init; }
    public string? ChangeId { get; init; }
}
