using LiteDB;

namespace NetPilot.Data.Documents;

public class UsageHistoryDocument
{
    [BsonId]
    public string Id { get; set; } = "";   // $"{Mac}|{MonthKey}"
    public string Mac { get; set; } = "";
    public string MonthKey { get; set; } = "";
    public long TotalBytes { get; set; }
    public DateTimeOffset FinalizedAtUtc { get; set; }
}
