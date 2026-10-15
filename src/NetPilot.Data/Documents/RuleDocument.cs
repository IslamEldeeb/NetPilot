using LiteDB;
using NetPilot.Core.Rules;

namespace NetPilot.Data.Documents;

public class RuleDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public string Payload { get; set; } = "";
}

public class RuleFiringDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Subject { get; set; } = "";
    public long FiredAtTicks { get; set; }
    public RuleFiringStatus Status { get; set; }
    public string Payload { get; set; } = "";
}
