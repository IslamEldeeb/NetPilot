using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;
using NetPilot.Core.Rules.Presentation;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Rules;

public record RulePreviewContext
{
    public IReadOnlyList<Device> Devices { get; init; } = [];
    public IReadOnlyList<DeviceUsageState> Usage { get; init; } = [];
    public IReadOnlySet<MacAddress> Protected { get; init; } = new HashSet<MacAddress>();
    public IReadOnlyList<RuleFiring> Firings { get; init; } = [];
    public IReadOnlyList<ActionClaim> Claims { get; init; } = [];
    public bool Enabled { get; init; } = true;
    public bool ForceDryRun { get; init; }
}

public record RuleSubjectPreview(string Subject, bool TriggerMatches, string Status,
    IReadOnlyList<string> Targets, IReadOnlyList<NotificationMessage> Notifications);
public record RulePreviewReport(bool DryRun, IReadOnlyList<RuleSubjectPreview> Subjects, string Limitations)
{
    public int TargetCount { get; init; }
    public int ProtectedCount { get; init; }
}

// Pure current-state projection: no providers, dispatchers or write-capable stores can enter this service.
public static class RulePreview
{
    public static RulePreviewReport Evaluate(Rule rule, RulePreviewContext context, UsagePeriodClock clock)
    {
        if (rule.Validate() is { Count: > 0 } errors) throw new ArgumentException(string.Join(' ', errors));
        var subjects = rule.Trigger is HouseholdUsageOver ? new Device?[] { null }
            : context.Devices.Where(rule.Scope.Matches).Cast<Device?>().ToArray();
        return new(rule.DryRun || context.ForceDryRun,
            subjects.Select(device => Subject(rule, device, context, clock)).ToArray(),
            "Saved state only; no writes or historical replay. Agent cadence, router availability, action budget and current wireless capabilities are not verified. Usage uses current Cairo periods; no exact future firing count.")
        {
            TargetCount = context.Devices.Count(rule.Scope.Matches),
            ProtectedCount = context.Devices.Count(d => rule.Scope.Matches(d) && context.Protected.Contains(d.Mac))
        };
    }

    private static RuleSubjectPreview Subject(Rule rule, Device? device, RulePreviewContext context, UsagePeriodClock clock)
    {
        var subject = device?.Mac.ToString() ?? "household";
        var match = RuleTriggerEvaluation.Evaluate(rule, device, context.Usage, clock);
        var previous = context.Firings.Where(f => f.RuleId == rule.Id && f.Subject == subject).OrderByDescending(f => f.FiredAtUtc).ToArray();
        var status = !context.Enabled ? "Rules kill switch is active" : !rule.Enabled ? "Rule is disabled"
            : previous.Any(f => f.Status == RuleFiringStatus.Active) ? "An active firing prevents another firing"
            : match is null ? "Trigger does not match now"
            : RuleRepeatSuppression.IsSuppressed(rule, match, previous.FirstOrDefault(), clock) ? "Repeat/episode/cooldown guard suppresses this firing"
            : "Would fire on an Agent evaluation of this saved state";
        var targets = device is null ? context.Devices.Where(rule.Scope.Matches).ToArray() : [device];
        var usage = UsageNumbers(rule, device, context, clock);
        var firing = new RuleFiring
        {
            RuleId = rule.Id, RuleName = rule.Name, Subject = subject, Actions = rule.Actions,
            DeviceName = device?.FriendlyName ?? device?.Hostname ?? "household", DryRun = rule.DryRun || context.ForceDryRun,
            UsedBytes = usage.Used, CapBytes = usage.Cap, TargetMacs = targets.Select(d => d.Mac).ToArray()
        };
        var protectedMacs = firing.TargetMacs.Where(context.Protected.Contains).ToArray();
        var notifications = rule.Actions.OfType<Notify>().Select(n => RuleNotificationText.Create(firing, n, protectedMacs)).ToArray();
        return new(device is null ? "Household" : RuleSummary.DeviceName(device), match is not null, status,
            targets.Select(d => Target(d, context)).ToArray(), notifications);
    }

    private static (decimal Used, long Cap) UsageNumbers(Rule rule, Device? device, RulePreviewContext context, UsagePeriodClock clock)
    {
        var threshold = rule.Trigger switch { UsageOver usage => (usage.Period, usage.Bytes), HouseholdUsageOver household => (household.Period, household.Bytes), _ => (RulePeriod.Day, 0L) };
        if (threshold.Item2 == 0) return (0, 0);
        var states = rule.Trigger is HouseholdUsageOver ? context.Usage : context.Usage.Where(s => s.Mac == device?.Mac);
        var used = states.Where(s => threshold.Item1 == RulePeriod.Day ? s.CurrentDayKey == clock.TodayKey() : s.CurrentMonthKey == clock.ThisMonthKey())
            .Sum(s => (decimal)(threshold.Item1 == RulePeriod.Day ? s.CurrentDayBytes : s.CurrentMonthBytes));
        return (used, threshold.Item2);
    }

    private static string LimitText(bool enabled, int? download, int? upload) => !enabled ? "unlimited"
        : $"download {(download is {} down ? down + " Kbps" : "unlimited")}, upload {(upload is {} up ? up + " Kbps" : "unlimited")}";

    private static string Target(Device device, RulePreviewContext context)
    {
        var effective = EffectiveRestriction.Compute(context.Claims.Where(c => c.Mac == device.Mac));
        var restriction = effective.IsBlocked ? "block claim active" : effective.Throttle is { } throttle
            ? $"automatic cap {throttle.DownloadKbps} Kbps" : "no automatic restriction";
        return $"{RuleSummary.DeviceName(device)} ({device.Mac}): {restriction}; manual block {(device.IsBlocked ? "recorded" : "not recorded")}; "
            + $"manual limit {(device.Override is {} manual ? LimitText(manual.Enabled, manual.DownloadKbps, manual.UploadKbps) : "none")}; router observation {(device.RouterReportedLimit is {} observed ? LimitText(observed.Enabled, observed.DownloadKbps, observed.UploadKbps) : "unavailable")}; base category limits can remain"
            + (context.Protected.Contains(device.Mac) ? "; protected: throttle/block skipped; notifications and guest/IoT changes still apply" : "");
    }
}
