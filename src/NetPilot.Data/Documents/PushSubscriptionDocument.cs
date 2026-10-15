namespace NetPilot.Data.Documents;

public class PushSubscriptionDocument
{
    public string Id { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? Label { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSuccessAtUtc { get; set; }
    public int FailureCount { get; set; }
}
