using NetPilot.Core.Quota;
using NetPilot.Core.Rules;

namespace NetPilot.Core.Automations;

public record AutomationChange(string Id, DateTimeOffset UndoUntilUtc);
public enum UndoOutcome { Restored, Conflict, Expired, AlreadyRestored, NotFound }

public interface IAutomationCommands
{
    Task<AutomationChange> SaveQuotaAsync(QuotaPolicy? before, QuotaPolicy? after, CancellationToken ct);
    Task<AutomationChange> SaveRuleAsync(Rule? before, Rule? after, CancellationToken ct);
    Task<UndoOutcome> UndoAsync(string changeId, CancellationToken ct);
    Task ImportRulesAsync(IReadOnlyList<Rule> rules, CancellationToken ct);
}
