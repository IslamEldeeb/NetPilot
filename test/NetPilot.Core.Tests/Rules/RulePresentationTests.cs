using System.Globalization;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Rules;
using NetPilot.Core.Rules.Presentation;

namespace NetPilot.Core.Tests.Rules;

public class RulePresentationTests
{
    private static readonly MacAddress Mac = new("aa:bb:cc:dd:ee:01");
    public static IEnumerable<object[]> Triggers()
    {
        yield return [new UsageOver(RulePeriod.Day, 5_000_000_000), "When all devices use over 5 GB today"];
        yield return [new UsageOver(RulePeriod.Month, 12_500_000_000), "When all devices use over 12.5 GB this month"];
        yield return [new HouseholdUsageOver(RulePeriod.Day, 500_000_000), "When the household uses over 0.5 GB today (actions target all devices)"];
        yield return [new HouseholdUsageOver(RulePeriod.Month, 200_000_000_000), "When the household uses over 200 GB this month (actions target all devices)"];
        yield return [new TimeWindow("22:00", "07:00", [DayOfWeek.Monday, DayOfWeek.Friday]), "During 22:00–07:00 on Monday, Friday for all devices"];
        yield return [new DeviceJoined(), "When a new device joins among all devices"];
        yield return [new DeviceIdle(14), "When all devices have been idle for 14 days"];
    }
    public static IEnumerable<object[]> Actions()
    {
        yield return [new Notify("Hello {device}", "Body"), "notify: Hello {device}"];
        yield return [new Throttle(256), "throttle to 256 Kbps"];
        yield return [new Block(), "block Internet access"];
        yield return [new SetWireless("guest-5g", false), "turn guest-5g off"];
        yield return [new SetWireless("iot-2.4g", true), "turn iot-2.4g on"];
    }

    public static IEnumerable<object[]> Summaries()
    {
        foreach (var trigger in Triggers())
        foreach (var action in Actions())
        foreach (var revert in Enum.GetValues<RuleRevertKind>())
        {
            if (revert == RuleRevertKind.WhenTriggerEnds && trigger[0] is not TimeWindow) continue;
            var end = revert switch
            {
                RuleRevertKind.None => "until released manually",
                RuleRevertKind.AfterMinutes => "release after 30 minutes",
                RuleRevertKind.WhenTriggerEnds => "release when the time window ends",
                _ => trigger[0] is UsageOver { Period: RulePeriod.Month } or HouseholdUsageOver { Period: RulePeriod.Month }
                    ? "release at the start of next month" : "release at midnight"
            };
            yield return [new Rule { Name = "Summary", Trigger = (RuleTrigger)trigger[0], Actions = [(RuleAction)action[0]], Revert = new(revert, 30) },
                $"{trigger[1]} → {action[1]}; {end}"];
        }
    }

    [Theory, MemberData(nameof(Summaries))]
    public void Summary_ExplainsEverySupportedTriggerActionAndRevert(Rule rule, string expected) => Assert.Equal(expected, RuleSummary.Describe(rule));

    [Fact]
    public void Summary_ExplainsScopesDryRunAndClaimOwners()
    {
        Assert.Equal("mobile devices", RuleSummary.Scope(new() { Kind = RuleScopeKind.Category, CategoryKey = "mobile" }));
        Assert.Equal("game console devices", RuleSummary.Scope(new() { Kind = RuleScopeKind.Category, CategoryKey = "game_console" }));
        Assert.Equal("selected devices (1)", RuleSummary.Scope(new() { Kind = RuleScopeKind.Devices, Devices = [Mac] }));
        var rule = new Rule { Name = "Bedtime", Actions = [new Throttle(256), new Block()] };
        Assert.Equal("Would have: throttle to 256 Kbps; block Internet access", RuleSummary.FiringActions(new() { DryRun = true, Actions = rule.Actions }));
        Assert.Equal("Rule: Bedtime holds a 256 Kbps throttle", RuleSummary.Claim(new(Mac, ClaimKind.Throttle, $"rule:{rule.Id}", 256, DateTimeOffset.UtcNow), [rule]));
        Assert.Equal("Quota: daily quota for 2026-10-16 holds a block", RuleSummary.Claim(new(Mac, ClaimKind.Block, "quota:day:2026-10-16", null, DateTimeOffset.UtcNow), []));
        Assert.Equal("Rule: Deleted rule holds a block", RuleSummary.Claim(new(Mac, ClaimKind.Block, $"rule:{Guid.NewGuid()}", null, DateTimeOffset.UtcNow, "Deleted rule"), []));
    }

    [Fact]
    public void FiringReason_UsesCapturedUsageAndWindowRatherThanEditedRule()
    {
        Assert.Equal("Internet usage at firing: 10.25 GB; threshold 10 GB", RuleSummary.FiringReason(new() { UsedBytes = 10_250_000_000m, CapBytes = 10_000_000_000 }));
        Assert.Equal("Matched time window 22:00–07:00", RuleSummary.FiringReason(new() { Window = new("22:00", "07:00", [DayOfWeek.Friday]) }));
        Assert.Equal("Device first discovered after rule creation", RuleSummary.FiringReason(new() { TriggerEpisodeKey = "joined" }));
        Assert.Equal("Device had not been seen for the configured idle duration", RuleSummary.FiringReason(new() { TriggerEpisodeKey = "idle:123" }));
    }

    [Theory]
    [InlineData("1", 1_000_000_000L)]
    [InlineData("0.000000001", 1L)]
    [InlineData("1.25", 1_250_000_000L)]
    [InlineData("9223372036.854775807", long.MaxValue)]
    public void DecimalGb_PreservesEveryByteRegardlessOfCulture(string input, long expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(RuleForm.TryBytes(input, out var bytes));
            Assert.Equal(expected, bytes);
            Assert.Equal(input, RuleForm.FormatGb(bytes));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("")][InlineData("0")][InlineData("-1")][InlineData("1,5")][InlineData("NaN")]
    [InlineData("1e2")][InlineData("0.0000000001")][InlineData("9223372036.854775808")][InlineData("999999999999999999999999999999")]
    public void InvalidGb_IsRejectedWithoutThrowing(string input) => Assert.False(RuleForm.TryBytes(input, out _));

    public static IEnumerable<object[]> Templates() => TemplateCatalog.All.Select(t => new object[] { t.Id });
    [Theory, MemberData(nameof(Templates))]
    public void Templates_RequireRelevantUserInputsAndThenValidate(string id)
    {
        var template = TemplateCatalog.All.Single(t => t.Id == id);
        var rule = template.Create();
        Assert.NotSame(rule, template.Create());
        Assert.NotEqual(rule.Id, template.Create().Id);
        Assert.True(rule.Enabled);
        Assert.True(rule.DryRun);
        Assert.NotEmpty(template.Description);
        Assert.NotEmpty(template.Hint);
        var form = RuleForm.From(rule);
        if (id is "bedtime" or "binge" or "household-cap" or "guest-night")
        {
            Assert.NotEmpty(rule.Validate());
            Assert.Null(form.Build(out var errors));
            Assert.Contains(errors, e => id switch
            {
                "bedtime" or "binge" => e.Contains("Scope"),
                "household-cap" => e.Contains("decimal GB"),
                _ => e.Contains("guest-*")
            });
        }
        switch (id)
        {
            case "bedtime": form.Devices.Add(Mac.ToString()); break;
            case "binge": form.Category = "television"; break;
            case "household-cap": form.Gigabytes = "200"; break;
            case "guest-night": form.Actions.Single().NetworkId = "guest-5g"; break;
        }
        var saved = form.Build(out var remaining);
        Assert.Empty(remaining);
        Assert.NotNull(saved);
        Assert.Empty(saved.Validate());
    }

    [Theory, MemberData(nameof(Triggers))]
    public void EditorRoundTrip_PreservesAllActionsScopesIdentityAndSettings(RuleTrigger trigger, string expectedTrigger)
    {
        foreach (var scope in new RuleScope[] { new(), new() { Kind = RuleScopeKind.Category, CategoryKey = "future_category" }, new() { Kind = RuleScopeKind.Devices, Devices = [Mac] } })
        foreach (var revert in Enum.GetValues<RuleRevertKind>())
        {
            if (revert == RuleRevertKind.WhenTriggerEnds && trigger is not TimeWindow) continue;
            var original = new Rule
            {
                Name = "Round trip", Enabled = false, DryRun = false, Scope = scope, Trigger = trigger,
                Actions = Actions().Select(a => (RuleAction)a[0]).ToArray(), Revert = new(revert, revert == RuleRevertKind.AfterMinutes ? 25 : 0),
                CooldownMinutes = 101, OncePer = RuleOncePer.Month, CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
            };
            var rebuilt = RuleForm.From(original).Build(out var errors);
            Assert.Empty(errors);
            Assert.NotNull(rebuilt);
            Assert.Equal(original.Id, rebuilt.Id);
            Assert.Equal(original.Name, rebuilt.Name);
            Assert.Equal(original.CreatedAtUtc, rebuilt.CreatedAtUtc);
            Assert.Equal(original.UpdatedAtUtc, rebuilt.UpdatedAtUtc);
            Assert.Equal(original.Enabled, rebuilt.Enabled);
            Assert.Equal(original.DryRun, rebuilt.DryRun);
            Assert.Equal(original.Scope.Kind, rebuilt.Scope.Kind);
            Assert.Equal(original.Scope.CategoryKey, rebuilt.Scope.CategoryKey);
            Assert.Equal(original.Scope.Devices, rebuilt.Scope.Devices);
            Assert.Equal(original.Actions, rebuilt.Actions);
            Assert.Equal(original.Revert, rebuilt.Revert);
            Assert.Equal(original.CooldownMinutes, rebuilt.CooldownMinutes);
            Assert.Equal(original.OncePer, rebuilt.OncePer);
            Assert.Equal(expectedTrigger, RuleSummary.Trigger(rebuilt.Trigger, "all devices"));
            if (trigger is TimeWindow window)
            {
                var rebuiltWindow = Assert.IsType<TimeWindow>(rebuilt.Trigger);
                Assert.Equal(window.Start, rebuiltWindow.Start);
                Assert.Equal(window.End, rebuiltWindow.End);
                Assert.Equal(window.Days.Order(), rebuiltWindow.Days);
            }
            else Assert.Equal(trigger, rebuilt.Trigger);
        }
    }

    [Fact]
    public void BadEditorInput_ReturnsEveryValidationErrorAndCannotSelectMainWifi()
    {
        var form = new RuleForm
        {
            Name = "", Scope = RuleScopeKind.Devices, Devices = ["bad-mac"], Trigger = RuleTriggerKind.UsageOver,
            Gigabytes = "no", CooldownMinutes = "bad", Revert = RuleRevertKind.WhenTriggerEnds,
            Actions = [new() { Kind = RuleActionKind.Throttle, Kbps = "bad" }, new() { Kind = RuleActionKind.SetWireless, NetworkId = "main-5g" }]
        };
        Assert.Null(form.Build(out var errors));
        foreach (var part in new[] { "Name", "MAC", "Scope", "decimal GB", "Cooldown", "Throttle Kbps", "time window", "main networks" })
            Assert.Contains(errors, e => e.Contains(part));
        Assert.All(RuleForm.SafeNetworkIds, id => Assert.True(Rule.IsSafeWireless(id)));
        Assert.DoesNotContain(RuleForm.SafeNetworkIds, id => id.StartsWith("main-"));
    }
}
