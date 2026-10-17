using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Rules;

public class RulePreviewAndPortabilityTests
{
    private static readonly MacAddress Mac = new("AA-BB-CC-DD-EE-01");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 20, 0, 0, TimeSpan.Zero);
    private static UsagePeriodClock Clock => new(timeProvider: new FixedTime());
    private static Device Device => new() { Mac = Mac, Hostname = "tv", CategoryKey = "television", FirstSeenAtUtc = Now.AddDays(-2) };
    private static Rule Rule => new() { Name = "Binge", Trigger = new UsageOver(RulePeriod.Day, 1_000_000_000), Actions = [new Notify("{rule}", "{device}: {used}/{cap}"), new Block()], DryRun = false };

    [Fact]
    public void Preview_UsesCurrentPeriodAndForcedMode_AndExplainsProtectionAndClaims()
    {
        var rule = Rule;
        var context = new RulePreviewContext
        {
            Devices = [Device], ForceDryRun = true, Protected = new HashSet<MacAddress> { Mac },
            Usage = [new() { Mac = Mac, CurrentDayKey = Clock.TodayKey(), CurrentDayBytes = 2_000_000_000 }],
            Claims = [new(Mac, ClaimKind.Throttle, "quota:day:today", 128, Now)]
        };
        var preview = RulePreview.Evaluate(rule, context, Clock);
        Assert.True(preview.DryRun);
        var subject = Assert.Single(preview.Subjects);
        Assert.True(subject.TriggerMatches);
        Assert.Contains("Would fire", subject.Status);
        Assert.Contains("128 Kbps", Assert.Single(subject.Targets));
        var notification = Assert.Single(subject.Notifications);
        Assert.Equal("[Dry-run] Binge", notification.Title);
        Assert.Contains("2000000000/1000000000", notification.Body);
        Assert.Contains("Protected devices: throttle/block skipped", notification.Body);
        Assert.Empty(notification.Actions);
        Assert.Single(context.Claims);
        Assert.Empty(context.Firings);
        Assert.False(Assert.Single(RulePreview.Evaluate(rule, context with { Usage = [new DeviceUsageState { Mac = Mac, CurrentDayKey = "2020-01-01", CurrentDayBytes = 2_000_000_000 }] }, Clock).Subjects).TriggerMatches);
    }

    [Theory]
    [InlineData("active", "active firing")]
    [InlineData("cooldown", "guard suppresses")]
    [InlineData("once", "guard suppresses")]
    [InlineData("disabled", "disabled")]
    [InlineData("kill", "kill switch")]
    public void Preview_HonorsExistingFiringAndRepeatGuards(string scenario, string expected)
    {
        var rule = Rule with { Enabled = scenario != "disabled", OncePer = scenario == "once" ? RuleOncePer.Day : RuleOncePer.None };
        var firing = new RuleFiring { RuleId = rule.Id, Subject = Mac, FiredAtUtc = scenario == "once" ? Now.AddHours(-5) : Now.AddMinutes(-1), DayKey = Clock.TodayKey(), Status = scenario == "active" ? RuleFiringStatus.Active : RuleFiringStatus.Released };
        var context = new RulePreviewContext { Devices = [Device], Enabled = scenario != "kill", Firings = [firing], Usage = [new() { Mac = Mac, CurrentDayKey = Clock.TodayKey(), CurrentDayBytes = 1_000_000_000 }] };
        Assert.Contains(expected, Assert.Single(RulePreview.Evaluate(rule, context, Clock).Subjects).Status);
    }

    [Fact]
    public void HouseholdPreview_SumsOutsideScope_ButOnlyTargetsScopedDevices()
    {
        var other = new Device { Mac = new("AA-BB-CC-DD-EE-02"), CategoryKey = "mobile" };
        var rule = Rule with { Trigger = new HouseholdUsageOver(RulePeriod.Month, 100), Scope = new() { Kind = RuleScopeKind.Category, CategoryKey = "television" } };
        var context = new RulePreviewContext { Devices = [Device, other], Usage = [new() { Mac = other.Mac, CurrentMonthKey = Clock.ThisMonthKey(), CurrentMonthBytes = 101 }] };
        var subject = Assert.Single(RulePreview.Evaluate(rule, context, Clock).Subjects);
        Assert.True(subject.TriggerMatches); Assert.Contains("tv", Assert.Single(subject.Targets));
        Assert.Contains("101/100", Assert.Single(subject.Notifications).Body);
    }

    [Fact]
    public void Import_RoundTripsEveryAction_AndForcesDryRunWithoutReplacingIds()
    {
        var rule = Rule with { Actions = [new Notify("{rule}", "{device}"), new Block(), new Throttle(512), new SetWireless("guest-5g", false)] };
        var imported = Assert.Single(RulePortability.Import(RulePortability.Export([rule]), []));
        Assert.True(imported.DryRun); Assert.Equal(rule.Id, imported.Id); Assert.Equal(rule.Actions, imported.Actions);
        Assert.Throws<ArgumentException>(() => RulePortability.Import(RulePortability.Export([rule]), [rule.Id]));
        Assert.Throws<ArgumentException>(() => RulePortability.Import(RulePortability.Export([rule, rule]), []));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"Version\":2,\"Rules\":[]}")]
    [InlineData("{\"Version\":1,\"Rules\":[{\"Name\":\"missing id\"}]}")]
    [InlineData("{\"Version\":1,\"Rules\":[{\"Id\":\"00000000-0000-0000-0000-000000000000\"}]}")]
    [InlineData("{\"Version\":1,\"Rules\":null}")]
    public void Import_RejectsUnsupportedSchemaAndMissingOrEmptyIds(string json) =>
        Assert.Throws<ArgumentException>(() => RulePortability.Import(json, []));

    [Fact]
    public void Import_RejectsUnsafeWirelessAndInvalidDeviceScope()
    {
        Assert.Throws<ArgumentException>(() => RulePortability.Import(RulePortability.Export([Rule with { Actions = [new SetWireless("main-5g", false)] }]), []));
        Assert.Throws<ArgumentException>(() => RulePortability.Import(RulePortability.Export([Rule with { Scope = new() { Kind = RuleScopeKind.Devices } }]), []));
    }

    [Fact]
    public void Import_EnforcesUtf8ByteLimitBeforeParsingLargeUnicodeInput()
    {
        var json = "{\"Version\":1,\"Rules\":[],\"Note\":\"" + new string('€', 333_333) + "\"}";
        Assert.Throws<ArgumentException>(() => RulePortability.Import(json, []));
    }

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
