using LiteDB;
using NetPilot.Core.Quota;

namespace NetPilot.Data.Documents;

public class QuotaPeriodStateDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public string Mac { get; set; } = "";
    public QuotaPeriodKind PeriodKind { get; set; }
    public string PeriodKey { get; set; } = "";
    public DateTime PeriodDateUtc { get; set; }
    public int[] WarnedPercents { get; set; } = [];
    public bool ExceededNotified { get; set; }
    public QuotaActionKind ActionKind { get; set; }
    public DateTimeOffset? ActionAppliedAtUtc { get; set; }
    public long ExtraAllowanceBytes { get; set; }
    public string[] UsedActionTokenHashes { get; set; } = [];
    public bool ReleaseRequested { get; set; }
    public bool ActionSuppressedForPeriod { get; set; }
    public bool? PriorLimitEnabled { get; set; }
    public int? PriorDownloadKbps { get; set; }
    public int? PriorUploadKbps { get; set; }
    public string? BlockToken { get; set; }
    public int? AppliedThrottleKbps { get; set; }
    public bool DryRunWouldHaveActed { get; set; }
    public bool ActionSkippedProtected { get; set; }
    public long CapBytes { get; set; }
    public long UsedBytes { get; set; }
}
