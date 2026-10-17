using LiteDB;

namespace NetPilot.Data.Documents;

public class UsageDailyHistoryDocument
{
    [BsonId]
    public string Id { get; set; } = "";   // $"{Mac}|{DayKey}"
    public string Mac { get; set; } = "";
    public string DayKey { get; set; } = "";
    public long TotalBytes { get; set; }
    public DateTimeOffset FinalizedAtUtc { get; set; }
    public long LanBytes { get; set; }
    public bool ForecastEligible { get; set; }
}
