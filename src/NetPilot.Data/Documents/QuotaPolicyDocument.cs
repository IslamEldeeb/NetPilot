using LiteDB;
using NetPilot.Core.Quota;

namespace NetPilot.Data.Documents;

public class QuotaPolicyDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public QuotaScope Scope { get; set; }
    public string? CategoryKey { get; set; }
    public string? Mac { get; set; }
    public long? DailyBytes { get; set; }
    public long? MonthlyBytes { get; set; }
    public bool Exempt { get; set; }
    public QuotaAction Action { get; set; }
    public int? ThrottleKbps { get; set; }
    public int[] WarnPercents { get; set; } = [80];
    public bool DryRun { get; set; } = true;
}
