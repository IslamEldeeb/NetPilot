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

    /// <summary>Null on any document written before this field existed — LitePolicyStore
    /// infers the value from LimitEnabled/DefinitionVersion in that case rather than
    /// silently treating a legacy row as unconfigured (which would stop enforcing every
    /// already-deployed policy the moment this ships).</summary>
    public bool? IsUserConfigured { get; set; }
}
