using LiteDB;

namespace NetPilot.Data.Documents;

public class UsageStateDocument
{
    [BsonId]
    public string Mac { get; set; } = "";
    public long LastRawCounterBytes { get; set; }
    public DateTimeOffset? LastPollAtUtc { get; set; }
    public string CurrentMonthKey { get; set; } = "";
    public long CurrentMonthBytes { get; set; }
}
