using LiteDB;

namespace NetPilot.Data.Documents;

public class PolicyDocument
{
    [BsonId]
    public string CategoryKey { get; set; } = "";
    public bool LimitEnabled { get; set; }
    public int? DownloadKbps { get; set; }
    public int? UploadKbps { get; set; }
    public int DefinitionVersion { get; set; } = 1;
}
