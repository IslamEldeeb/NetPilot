using LiteDB;

namespace NetPilot.Data.Documents;

public class ClaimDeviceStateDocument
{
    [BsonId]
    public string Mac { get; set; } = "";
    public bool? PriorLimitEnabled { get; set; }
    public int? PriorDownloadKbps { get; set; }
    public int? PriorUploadKbps { get; set; }
    public string? BlockToken { get; set; }
    public bool UnblockProjectionPending { get; set; }
}
