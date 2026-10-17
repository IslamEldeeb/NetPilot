using LiteDB;
using NetPilot.Core.Enforcement.Claims;

namespace NetPilot.Data.Documents;

public class ActionClaimDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public string Mac { get; set; } = "";
    public ClaimKind Kind { get; set; }
    public string Source { get; set; } = "";
    public int? ThrottleKbps { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool Pending { get; set; }
    public bool DryRun { get; set; }
}
