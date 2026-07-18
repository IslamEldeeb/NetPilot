namespace NetPilot.Data.Documents;

public class ActivityLogDocument
{
    public int Id { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Type { get; set; } = "";
    public string? Mac { get; set; }
    public string Message { get; set; } = "";
}
