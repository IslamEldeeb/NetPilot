using System.Globalization;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;

namespace NetPilot.Core.Rules.Presentation;

public static class RuleSummary
{
    public static string Describe(Rule rule) => $"{Trigger(rule.Trigger, Scope(rule.Scope))} → {Actions(rule.Actions)}; {Revert(rule.Revert, rule.Trigger)}";

    public static string Scope(RuleScope scope) => scope.Kind switch
    {
        RuleScopeKind.All => "all devices",
        RuleScopeKind.Category => $"{scope.CategoryKey?.Replace('_', ' ')} devices",
        RuleScopeKind.Devices => $"selected devices ({scope.Devices.Length})",
        _ => "unknown scope"
    };

    public static string Trigger(RuleTrigger trigger, string scope) => trigger switch
    {
        UsageOver u => $"When {scope} use over {RuleForm.FormatGb(u.Bytes)} GB {Period(u.Period)}",
        HouseholdUsageOver h => $"When the household uses over {RuleForm.FormatGb(h.Bytes)} GB {Period(h.Period)} (actions target {scope})",
        TimeWindow w => $"During {w.Start}–{w.End} on {string.Join(", ", w.Days)} for {scope}",
        DeviceJoined => $"When a new device joins among {scope}",
        DeviceIdle i => $"When {scope} have been idle for {i.Days} days",
        _ => "Unknown trigger"
    };

    public static string Action(RuleAction action) => action switch
    {
        Notify n => $"notify: {n.Title}",
        Throttle t => $"throttle to {t.Kbps.ToString(CultureInfo.InvariantCulture)} Kbps",
        Block => "block Internet access",
        SetWireless w => $"turn {w.NetworkId} {(w.Enabled ? "on" : "off")}",
        _ => "unknown action"
    };

    public static string Actions(IEnumerable<RuleAction> actions) => string.Join("; ", actions.Select(Action));
    public static string FiringReason(RuleFiring firing) => firing.CapBytes > 0
        ? $"Internet usage at firing: {NetPilot.Core.Usage.DataSize.ToGb(firing.UsedBytes).ToString("0.#########", CultureInfo.InvariantCulture)} GB; threshold {RuleForm.FormatGb(firing.CapBytes)} GB"
        : firing.Window is { } window ? $"Matched time window {window.Start}–{window.End}"
        : firing.TriggerEpisodeKey == "joined" ? "Device first discovered after rule creation"
        : firing.TriggerEpisodeKey.StartsWith("idle:", StringComparison.Ordinal) ? "Device had not been seen for the configured idle duration"
        : "Rule trigger matched";

    public static string FiringActions(RuleFiring firing) => (firing.DryRun ? "Would have: " : "Actions: ") + Actions(firing.Actions);
    public static string Revert(RuleRevert revert, RuleTrigger trigger) => revert.Kind switch
    {
        RuleRevertKind.None => "until released manually",
        RuleRevertKind.AfterMinutes => $"release after {revert.Minutes} minutes",
        RuleRevertKind.WhenTriggerEnds => "release when the time window ends",
        RuleRevertKind.AtPeriodEnd => trigger is UsageOver { Period: RulePeriod.Month } or HouseholdUsageOver { Period: RulePeriod.Month }
            ? "release at the start of next month" : "release at midnight",
        _ => "unknown revert policy"
    };

    public static string Claim(ActionClaim claim, IReadOnlyList<Rule> rules)
    {
        var owner = claim.Source.StartsWith("rule:", StringComparison.Ordinal)
            ? "Rule: " + (rules.FirstOrDefault(r => $"rule:{r.Id}" == claim.Source)?.Name ?? claim.Reason ?? claim.Source)
            : claim.Source.StartsWith("quota:", StringComparison.Ordinal) ? "Quota: " + QuotaOwner(claim.Source[6..]) : claim.Source;
        return $"{owner} holds {(claim.Kind == ClaimKind.Block ? "a block" : $"a {claim.ThrottleKbps} Kbps throttle")}";
    }

    private static string QuotaOwner(string source)
    {
        var parts = source.Split(':', 2);
        return parts.Length == 2 && parts[0] is "day" or "month"
            ? $"{(parts[0] == "day" ? "daily" : "monthly")} quota for {parts[1]}" : source;
    }

    public static string DeviceName(Device device) => !string.IsNullOrWhiteSpace(device.FriendlyName) ? device.FriendlyName
        : !string.IsNullOrWhiteSpace(device.Hostname) ? device.Hostname : device.Mac.ToString();
    private static string Period(RulePeriod period) => period == RulePeriod.Month ? "this month" : "today";
}
