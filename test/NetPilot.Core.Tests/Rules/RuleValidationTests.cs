using NetPilot.Core.Devices;
using NetPilot.Core.Rules;

namespace NetPilot.Core.Tests.Rules;

public class RuleValidationTests
{
    public static IEnumerable<object[]> InvalidRules()
    {
        var valid = new Rule { Name = "Rule", Actions = [new Notify("title", "body")] };
        yield return [valid with { Name = " " }, "Name"];
        yield return [valid with { Name = new string('x', 81) }, "80"];
        yield return [valid with { Id = Guid.Empty }, "ID"];
        yield return [valid with { Actions = [] }, "action"];
        yield return [valid with { Actions = [new Throttle(0)] }, "Kbps"];
        yield return [valid with { Actions = [new Throttle(-1)] }, "Kbps"];
        yield return [valid with { Trigger = new UsageOver(RulePeriod.Day, 0) }, "bytes"];
        yield return [valid with { Trigger = new HouseholdUsageOver(RulePeriod.Month, -1) }, "bytes"];
        yield return [valid with { Trigger = new UsageOver((RulePeriod)100, 1) }, "period"];
        yield return [valid with { Trigger = new DeviceIdle(0) }, "Idle"];
        yield return [valid with { Trigger = new TimeWindow("7:00", "08:00", [DayOfWeek.Monday]) }, "HH:mm"];
        yield return [valid with { Trigger = new TimeWindow("07:00", "25:00", [DayOfWeek.Monday]) }, "HH:mm"];
        yield return [valid with { Trigger = new TimeWindow("07:00", "07:00", [DayOfWeek.Monday]) }, "distinct"];
        yield return [valid with { Trigger = new TimeWindow("07:00", "08:00", []) }, "weekday"];
        yield return [valid with { Trigger = new TimeWindow("07:00", "08:00", [(DayOfWeek)100]) }, "weekday"];
        yield return [valid with { Scope = new() { Kind = RuleScopeKind.Category } }, "Scope"];
        yield return [valid with { Scope = new() { Kind = RuleScopeKind.Devices } }, "Scope"];
        yield return [valid with { Scope = new() { Kind = RuleScopeKind.Devices, Devices = [default(MacAddress)] } }, "Scope"];
        yield return [valid with { Revert = new(RuleRevertKind.WhenTriggerEnds) }, "TimeWindow"];
        yield return [valid with { Revert = new(RuleRevertKind.AfterMinutes, 0) }, "delay"];
        yield return [valid with { Actions = [new SetWireless("main-5g", false)] }, "main"];
        yield return [valid with { Actions = [new SetWireless("guest-invalid", false)] }, "guest"];
        yield return [valid with { CooldownMinutes = -1 }, "Cooldown"];
        yield return [valid with { OncePer = (RuleOncePer)100 }, "once-per"];
    }

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void InvalidInput_ReturnsHumanReadableError(Rule rule, string error) =>
        Assert.Contains(rule.Validate(), e => e.Contains(error, StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("guest-5g")]
    [InlineData("iot-2.4g")]
    public void SafeWirelessAndWindowRevert_AreValid(string networkId)
    {
        var rule = new Rule { Name = "Night", Trigger = new TimeWindow("22:00", "07:00", [DayOfWeek.Friday]),
            Actions = [new SetWireless(networkId, false)], Revert = new(RuleRevertKind.WhenTriggerEnds) };
        Assert.Empty(rule.Validate());
    }
}
