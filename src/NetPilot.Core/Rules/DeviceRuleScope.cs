namespace NetPilot.Core.Rules;

/// <summary>Scope preview only: rules whose scope matches a device. It is not a guarantee they will fire.</summary>
public static class DeviceRuleScope
{
    public static IReadOnlyList<Rule> Matching(IReadOnlyList<Rule> rules, Devices.Device device) =>
        rules.Where(rule => rule.Scope.Matches(device)).ToArray();
}
