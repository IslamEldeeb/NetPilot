namespace NetPilot.Data.Documents;

public class ActivityLogDocument
{
    public int Id { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Type { get; set; } = "";
    public string? Mac { get; set; }
    public string? Source { get; set; }
    public string? Actor { get; set; }
    public string? CorrelationId { get; set; }
    public string? DeduplicationKey { get; set; }
    public string? Kind { get; set; }
    public bool? DryRun { get; set; }
    public bool Failed { get; set; }
    public bool IsNotification { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
    public string? ChangeId { get; set; }
    public string Message { get; set; } = "";
}
