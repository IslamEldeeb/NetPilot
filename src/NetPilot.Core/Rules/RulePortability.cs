using System.Text;
using System.Text.Json;

namespace NetPilot.Core.Rules;

public record RulesExport(int Version, Rule[] Rules);

public static class RulePortability
{
    public static string Export(IEnumerable<Rule> rules) =>
        JsonSerializer.Serialize(new RulesExport(1, rules.ToArray()), RuleSerialization.Options);

    public static IReadOnlyList<Rule> Import(string json, IEnumerable<Guid> existingIds)
    {
        if (Encoding.UTF8.GetByteCount(json) > 1_000_000) throw new ArgumentException("Rules JSON must be at most 1 MB.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Choose a versioned rules object.");
        if (!document.RootElement.TryGetProperty("Rules", out var ruleElements) || ruleElements.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Rules must be an array with explicit rule IDs.");
        foreach (var element in ruleElements.EnumerateArray())
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("Id", out var id)
                || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var parsed) || parsed == Guid.Empty)
                throw new ArgumentException("Every imported rule needs a non-empty GUID ID.");
        var export = JsonSerializer.Deserialize<RulesExport>(json, RuleSerialization.Options)
            ?? throw new ArgumentException("Choose a rules export.");
        if (export.Version != 1 || export.Rules is null || export.Rules.Length > 200)
            throw new ArgumentException("Unsupported version or more than 200 rules.");
        var ids = existingIds.ToHashSet();
        foreach (var rule in export.Rules)
        {
            if (rule is null || rule.Validate().Count > 0)
                throw new ArgumentException("Import contains an invalid rule. Check its scope, trigger and actions.");
            if (!ids.Add(rule.Id)) throw new ArgumentException("Duplicate or existing rule ID. Import never overwrites a rule.");
        }
        return export.Rules.Select(r => r with { DryRun = true }).ToArray();
    }
}
