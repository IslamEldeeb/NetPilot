using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetPilot.Core.Rules;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteRuleStore(NetPilotDatabase db, ILogger<LiteRuleStore>? logger = null) : IRuleStore
{
    private readonly ILiteCollection<RuleDocument> _rules = db.GetCollection<RuleDocument>("rules");
    private readonly ILogger<LiteRuleStore> _logger = logger ?? NullLogger<LiteRuleStore>.Instance;

    public Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Rule>>(
        _rules.FindAll().Select(Read).OfType<Rule>().ToArray());

    public Task<Rule?> FindAsync(Guid id, CancellationToken ct)
    {
        var doc = _rules.FindById(id.ToString());
        return Task.FromResult(doc is null ? null : Read(doc));
    }

    public Task UpsertAsync(Rule rule, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var errors = rule.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(rule));
        db.InTransaction(() =>
        {
            _rules.Upsert(new RuleDocument { Id = rule.Id.ToString(), Payload = JsonSerializer.Serialize(rule, RuleJson.Options) });
            AutomationRevision.Advance(db, "rule:" + rule.Id);
        });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct)
    {
        db.InTransaction(() =>
        {
            _rules.Delete(id.ToString());
            AutomationRevision.Advance(db, "rule:" + id);
        });
        return Task.CompletedTask;
    }

    private Rule? Read(RuleDocument doc)
    {
        try
        {
            var rule = JsonSerializer.Deserialize<Rule>(doc.Payload, RuleJson.Options);
            if (rule is null || rule.Id.ToString() != doc.Id || rule.Validate().Count > 0)
                throw new JsonException("Invalid rule data.");
            return rule;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            _logger.LogWarning("Skipping unreadable rule {RuleId}: {ErrorType}", doc.Id, ex.GetType().Name);
            return null;
        }
    }
}
