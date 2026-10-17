using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Rules;

// The Agent serializes evaluations; Web edits rules and release requests, never router state.
public partial class RuleEvaluationService(
    IRuleStore ruleStore,
    IRuleFiringStore firingStore,
    IDeviceStore deviceStore,
    IUsageStore usageStore,
    IProtectedDeviceStore protectedDevices,
    IActionClaimStore claimStore,
    ClaimEnforcementService claimEnforcement,
    NotificationDispatcher notifications,
    IActivityLogStore activityLog,
    UsagePeriodClock clock,
    RulesOptions options,
    Settings.IPreferencesStore? preferences = null)
{
    public async Task EvaluateAsync(IRouterProvider provider, IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        options.Validate();
        var budget = new RuleRouterBudget(provider, options.MaxRouterActionsPerTick);
        var rules = (await ruleStore.GetAllAsync(ct)).ToDictionary(r => r.Id);
        await RevertAsync(budget, rules, ct);
        if (!options.Enabled) return;

        var active = (await firingStore.GetAllAsync(ct)).Where(f => f.Status == RuleFiringStatus.Active).ToList();
        foreach (var pending in active.Where(f => !f.Completed && !f.Reverting))
            await ResumeSafelyAsync(budget, pending, ct);

        var devices = await deviceStore.GetAllAsync(ct);
        var usage = await usageStore.GetAllStatesAsync(ct);
        foreach (var rule in rules.Values.Where(r => r.Enabled))
        {
            try
            {
                var errors = rule.Validate();
                if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
                var subjects = rule.Trigger is HouseholdUsageOver ? new Device?[] { null }
                    : devices.Where(rule.Scope.Matches).Cast<Device?>().ToArray();
                foreach (var device in subjects)
                    await EvaluateSubjectSafelyAsync(budget, (rule, device), (devices, usage), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                await LogAsync(ActivityEventType.RuleFailed, (rule.Name, rule.Id.ToString(), rule.DryRun || options.ForceDryRun), null, $"evaluation failed: {ex.Message}", ct);
            }
        }
        var retention = preferences is null ? new Settings.RetentionPreferences() : await preferences.GetRetentionAsync(ct);
        await firingStore.PruneAsync(retention.RuleFirings, ct);
    }

    private async Task EvaluateSubjectSafelyAsync(IRouterProvider provider, (Rule Rule, Device? Device) subject,
        (IReadOnlyList<Device> Devices, IReadOnlyList<DeviceUsageState> Usage) context, CancellationToken ct)
    {
        var (rule, device) = subject;
        try
        {
            ct.ThrowIfCancellationRequested();
            var key = device?.Mac.ToString() ?? "household";
            if ((await firingStore.GetAllAsync(ct)).Any(f => f.RuleId == rule.Id && f.Subject == key && f.Status == RuleFiringStatus.Active))
                return;
            var match = RuleTriggerEvaluation.Evaluate(rule, device, context.Usage, clock);
            if (match is null || Suppressed(rule, match, await firingStore.FindLatestAsync(rule.Id, key, ct))) return;
            var targets = device is null ? context.Devices.Where(rule.Scope.Matches).Select(d => d.Mac).ToArray() : new[] { device.Mac };
            var firing = NewFiring(rule, device, match, targets);
            await SaveAsync(firing, ct);
            await ResumeSafelyAsync(provider, firing, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await LogAsync(ActivityEventType.RuleFailed, (rule.Name, rule.Id.ToString(), rule.DryRun || options.ForceDryRun), device?.Mac, $"evaluation failed: {ex.Message}", ct);
        }
    }

    private bool Suppressed(Rule rule, TriggerMatch match, RuleFiring? latest) =>
        RuleRepeatSuppression.IsSuppressed(rule, match, latest, clock);

    private RuleFiring NewFiring(Rule rule, Device? device, TriggerMatch match, MacAddress[] targets)
    {
        var now = clock.GetUtcNow();
        var period = rule.Trigger switch
        {
            UsageOver u => u.Period,
            HouseholdUsageOver h => h.Period,
            _ => RulePeriod.Day
        };
        var stableEpisode = rule.Trigger is TimeWindow or DeviceJoined or DeviceIdle;
        return new()
        {
            RuleId = rule.Id, RuleName = rule.Name, Subject = device?.Mac.ToString() ?? "household",
            EpisodeKey = stableEpisode ? match.EpisodeKey : $"{match.EpisodeKey}:{now.UtcTicks}",
            TriggerEpisodeKey = match.EpisodeKey, FiredAtUtc = now, DayKey = clock.DayKey(now), MonthKey = clock.MonthKey(now),
            DryRun = rule.DryRun || options.ForceDryRun, Actions = rule.Actions.Distinct().ToArray(), TargetMacs = targets,
            DeviceName = device?.FriendlyName ?? device?.Hostname ?? "household", UsedBytes = match.Used, CapBytes = match.Cap,
            RevertAtUtc = rule.Revert.Kind == RuleRevertKind.AfterMinutes ? now.AddMinutes(rule.Revert.Minutes) : null,
            RevertWhenTriggerEnds = rule.Revert.Kind == RuleRevertKind.WhenTriggerEnds,
            Window = rule.Trigger as TimeWindow,
            RevertPeriod = rule.Revert.Kind == RuleRevertKind.AtPeriodEnd ? period : null,
            RevertPeriodKey = rule.Revert.Kind == RuleRevertKind.AtPeriodEnd
                ? (period == RulePeriod.Day ? clock.DayKey(now) : clock.MonthKey(now)) : null
        };
    }

    private async Task ResumeSafelyAsync(IRouterProvider provider, RuleFiring firing, CancellationToken ct)
    {
        try { await ApplyActionsAsync(provider, firing, ct); }
        catch (RuleActionDeferredException) { /* Durable action progress resumes before new firings next tick. */ }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var current = await firingStore.FindAsync(firing.Id, ct) ?? firing;
            await SaveAsync(current with { Outcome = RuleFiringOutcome.Failed, Error = ex.Message }, ct);
            await LogAsync(ActivityEventType.RuleFailed, (firing.RuleName, firing.Id, firing.DryRun), SubjectMac(firing), $"action failed: {ex.Message}", ct);
        }
    }

    private Task<RuleFiring> SaveAsync(RuleFiring firing, CancellationToken ct) => firingStore.UpdateAsync(firing.Id,
        current => firing with { ReleaseRequested = firing.Status == RuleFiringStatus.Active
            && (firing.ReleaseRequested || current?.ReleaseRequested == true) }, ct);

    private Task LogAsync(ActivityEventType type, (string Name, string Correlation, bool DryRun) rule, MacAddress? mac, string message, CancellationToken ct) =>
        activityLog.AppendAsync(new(clock.GetUtcNow(), type, mac, $"Rule '{rule.Name}': {message}")
        { Source = "rules", Actor = "Agent", Kind = "rule", CorrelationId = rule.Correlation, DryRun = rule.DryRun, Failed = type == ActivityEventType.RuleFailed }, ct);

    private static MacAddress? SubjectMac(RuleFiring firing) => firing.Subject == "household" ? null : new MacAddress(firing.Subject);
    private static string Source(Guid id) => $"rule:{id}";
}
