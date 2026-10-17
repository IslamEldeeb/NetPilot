using NetPilot.Core.Devices;
using NetPilot.Core.Rules;

namespace NetPilot.Core.Tests.Rules;

public class DeviceRuleScopeTests
{
    private static Device Device(string category) => new()
    {
        Mac = new("AA-BB-CC-DD-EE-01"),
        Hostname = "tv",
        CategoryKey = category,
        FirstSeenAtUtc = DateTimeOffset.UtcNow
    };

    private static Rule Rule(RuleScope scope) => new()
    {
        Id = Guid.NewGuid(),
        Name = "rule",
        Scope = scope,
        Actions = [new Block()],
        Revert = new(RuleRevertKind.None)
    };

    [Fact]
    public void Matching_ReturnsOnlyRulesWhoseScopeMatches()
    {
        var rules = new[]
        {
            Rule(new RuleScope { Kind = RuleScopeKind.All }),
            Rule(new RuleScope { Kind = RuleScopeKind.Category, CategoryKey = "television" }),
            Rule(new RuleScope { Kind = RuleScopeKind.Category, CategoryKey = "mobile" }),
            Rule(new RuleScope { Kind = RuleScopeKind.Devices, Devices = [new("AA-BB-CC-DD-EE-01")] }),
            Rule(new RuleScope { Kind = RuleScopeKind.Devices, Devices = [new("02-00-00-00-00-02")] })
        };

        var matching = DeviceRuleScope.Matching(rules, Device("television"));

        Assert.Equal(3, matching.Count);
    }
}
