using LiteDB;

namespace NetPilot.Data.Documents;

public class DeviceDocument
{
    [BsonId]
    public string Mac { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string? FriendlyName { get; set; }
    public string IpAddress { get; set; } = "";
    public string CategoryKey { get; set; } = "";
    public string ConnectionMedium { get; set; } = "Unknown";
    public bool IsOnline { get; set; }
    public bool? OverrideEnabled { get; set; }
    public int? OverrideDownloadKbps { get; set; }
    public int? OverrideUploadKbps { get; set; }
    public bool? RouterLimitEnabled { get; set; }
    public int? RouterDownloadKbps { get; set; }
    public int? RouterUploadKbps { get; set; }
    public bool? RouterLimitCurrentlyEnforced { get; set; }
    public string? LastAppliedFingerprint { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
