using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;

namespace NetPilot.Core.Rules;

public partial class RuleEvaluationService
{
    private async Task RevertAsync(IRouterProvider provider, IReadOnlyDictionary<Guid, Rule> rules, CancellationToken ct)
    {
        foreach (var firing in (await firingStore.GetAllAsync(ct)).Where(f => f.Status == RuleFiringStatus.Active))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                rules.TryGetValue(firing.RuleId, out var rule);
                if (await ShouldRevertAsync(firing, rule, ct))
                    await RevertFiringAsync(provider, firing, rule is null || !rule.Enabled || !options.Enabled, ct);
            }
            catch (RuleActionDeferredException) { /* Reverts retain priority on the next tick. */ }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var current = await firingStore.FindAsync(firing.Id, ct) ?? firing;
                await SaveAsync(current with { Outcome = RuleFiringOutcome.Failed, Error = ex.Message }, ct);
                await LogAsync(ActivityEventType.RuleFailed, firing.RuleName, SubjectMac(firing), $"revert failed: {ex.Message}", ct);
            }
        }
    }

    private async Task<bool> ShouldRevertAsync(RuleFiring firing, Rule? rule, CancellationToken ct)
    {
        if (firing.Reverting || firing.ReleaseRequested || !options.Enabled || rule is null || !rule.Enabled) return true;
        if (!firing.DryRun && (options.ForceDryRun || rule.DryRun)) return true;
        if (rule.Validate().Count > 0) return true;
        foreach (var mac in firing.TargetMacs)
        {
            var device = await deviceStore.FindByMacAsync(mac, ct);
            if (device is null || !rule.Scope.Matches(device)) return true;
            if (firing.AppliedClaimIds.Length > 0 && await protectedDevices.IsProtectedAsync(mac, ct)) return true;
        }
        if (firing.RevertAtUtc <= clock.GetUtcNow()) return true;
        if (firing.RevertPeriod is { } period && firing.RevertPeriodKey !=
            (period == RulePeriod.Day ? clock.TodayKey() : clock.ThisMonthKey())) return true;
        return firing.RevertWhenTriggerEnds && (firing.Window is null ||
            RuleTriggerEvaluation.WindowEpisode(firing.Window, clock.GetUtcNow(), clock) != firing.TriggerEpisodeKey);
    }

    private async Task RevertFiringAsync(IRouterProvider provider, RuleFiring initial, bool releaseSource, CancellationToken ct)
    {
        var firing = await SaveAsync(initial with { Reverting = true }, ct);
        if (releaseSource) await claimStore.RemoveBySourceAsync(Source(firing.RuleId), ct);
        foreach (var claimId in firing.AppliedClaimIds)
            await claimStore.RemoveAsync(claimId, ct);
        foreach (var mac in firing.TargetMacs.Where(m => firing.AppliedClaimIds.Any(id => id.StartsWith($"{m}|", StringComparison.Ordinal))))
        {
            var device = await deviceStore.FindByMacAsync(mac, ct)
                ?? throw new InvalidOperationException("Cannot restore a device missing from the device store.");
            if (!await ReconcileClaimsAsync(provider, device, ct))
                throw new InvalidOperationException("Claim restore did not complete; will retry next tick.");
        }
        foreach (var prior in firing.WirelessPrior.Reverse())
            await RestoreWirelessAsync(provider, firing, prior, ct);
        await SaveAsync(firing with { Status = firing.ReleaseRequested ? RuleFiringStatus.Released : RuleFiringStatus.Reverted,
            Completed = true, Error = null }, ct);
        await LogAsync(ActivityEventType.RuleReverted, firing.RuleName, SubjectMac(firing), $"released effects for {firing.Subject}", ct);
    }

    private async Task RestoreWirelessAsync(IRouterProvider provider, RuleFiring firing, WirelessPrior prior, CancellationToken ct)
    {
        if (!Rule.IsSafeWireless(prior.NetworkId)) throw new InvalidOperationException("Unsafe wireless network in revert record.");
        var id = WirelessNetworkId.Parse(prior.NetworkId);
        var current = (await provider.GetWirelessNetworksAsync(ct)).Single(n => n.Id == id);
        if (current.Enabled != prior.AppliedEnabled)
        {
            await LogAsync(ActivityEventType.RuleReverted, firing.RuleName, SubjectMac(firing),
                $"left {prior.NetworkId} unchanged: current enabled state differs from the rule's last write", ct);
            return;
        }
        if (current.Enabled == prior.PriorEnabled) return;
        var restored = await provider.ApplyWirelessAsync(id, new() { Enabled = prior.PriorEnabled }, ct);
        if (restored.Enabled != prior.PriorEnabled) throw new InvalidOperationException("Router did not confirm the wireless restore.");
    }
}
