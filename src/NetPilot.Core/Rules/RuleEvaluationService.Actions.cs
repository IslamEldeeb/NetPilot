using System.Globalization;
using System.Text.RegularExpressions;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;

namespace NetPilot.Core.Rules;

public partial class RuleEvaluationService
{
    private async Task ApplyActionsAsync(IRouterProvider provider, RuleFiring firing, CancellationToken ct)
    {
        if (firing.Outcome == RuleFiringOutcome.Failed) firing = firing with { Error = null };
        for (var i = 0; i < firing.Actions.Length; i++)
        {
            if (firing.ReleaseRequested || firing.Reverting) return;
            firing = await ApplyActionAsync(provider, firing, (firing.Actions[i], i), ct);
        }
        if (firing.ReleaseRequested) return;
        firing = await NotifySafetyOutcomeAsync(firing, ct);
        await CompleteFiringAsync(firing, ct);
    }

    private async Task<RuleFiring> ApplyActionAsync(IRouterProvider provider, RuleFiring firing,
        (RuleAction Action, int Index) effect, CancellationToken ct)
    {
        var (action, index) = effect;
        if (action is Throttle or Block)
        {
            foreach (var mac in firing.TargetMacs)
            {
                if (firing.ReleaseRequested) return firing;
                firing = await ApplyClaimAsync(provider, firing, (action, $"{index}:{mac}", mac), ct);
            }
            return firing;
        }
        var key = index.ToString(CultureInfo.InvariantCulture);
        if (firing.CompletedActionKeys.Contains(key)) return firing;
        if (action is Notify notify)
        {
            // Persist notification consumption before dispatch, as the quota event path does.
            firing = await CompleteActionAsync(firing, key, ct);
            await NotifyAsync(firing, notify.Title, notify.Body, ct);
            return firing;
        }
        if (action is SetWireless wireless && !firing.DryRun)
            firing = await ApplyWirelessAsync(provider, firing, wireless, ct);
        return await CompleteActionAsync(firing, key, ct);
    }

    private async Task<RuleFiring> NotifySafetyOutcomeAsync(RuleFiring firing, CancellationToken ct)
    {
        var dryRunNotice = firing.DryRun && !firing.Actions.OfType<Notify>().Any();
        if (!dryRunNotice && !firing.SkippedProtected) return firing;
        var key = dryRunNotice ? "dry-run-notice" : "protected-notice";
        if (firing.CompletedActionKeys.Contains(key)) return firing;
        firing = await CompleteActionAsync(firing, key, ct);
        await NotifyAsync(firing, firing.RuleName, dryRunNotice ? "Rule matched." : "Protected device: throttle/block skipped.", ct);
        return firing;
    }

    private async Task CompleteFiringAsync(RuleFiring firing, CancellationToken ct)
    {
        var outcome = firing.SkippedProtected ? RuleFiringOutcome.SkippedProtected
            : firing.DryRun ? RuleFiringOutcome.DryRun : RuleFiringOutcome.Applied;
        firing = await SaveAsync(firing with { Completed = true, Outcome = outcome,
            Status = firing.AppliedClaimIds.Length == 0 && firing.WirelessPrior.Length == 0
                ? RuleFiringStatus.Released : RuleFiringStatus.Active }, ct);
        await LogAsync(outcome switch
        {
            RuleFiringOutcome.DryRun => ActivityEventType.RuleDryRun,
            RuleFiringOutcome.SkippedProtected => ActivityEventType.RuleActionSkippedProtected,
            _ => ActivityEventType.RuleFired
        }, firing.RuleName, SubjectMac(firing), outcome == RuleFiringOutcome.DryRun
            ? "would have performed: " + DescribeActions(firing) : $"{outcome} for {firing.Subject}", ct);
    }

    private async Task<RuleFiring> ApplyClaimAsync(IRouterProvider provider, RuleFiring firing,
        (RuleAction Action, string Key, MacAddress Mac) effect, CancellationToken ct)
    {
        if (firing.CompletedActionKeys.Contains(effect.Key)) return firing;
        var device = await deviceStore.FindByMacAsync(effect.Mac, ct)
            ?? throw new InvalidOperationException("Cannot restrict a device missing from the device store.");
        if (await protectedDevices.IsProtectedAsync(device.Mac, ct))
            return await CompleteActionAsync(firing with { SkippedProtected = true }, effect.Key, ct);
        if (firing.DryRun) return await CompleteActionAsync(firing, effect.Key, ct);
        if (provider is RuleRouterBudget budget) budget.RequireCapacity();
        var claim = new ActionClaim(device.Mac, effect.Action is Block ? ClaimKind.Block : ClaimKind.Throttle,
            Source(firing.RuleId), (effect.Action as Throttle)?.Kbps, firing.FiredAtUtc, firing.RuleName);
        var existing = (await claimStore.GetForDeviceAsync(device.Mac, ct)).FirstOrDefault(c => c.Id == claim.Id);
        if (existing?.ThrottleKbps is { } cap && claim.ThrottleKbps is { } requested)
            claim = claim with { ThrottleKbps = Math.Min(cap, requested) };
        if (!firing.AppliedClaimIds.Contains(claim.Id))
            firing = await SaveAsync(firing with { AppliedClaimIds = [.. firing.AppliedClaimIds, claim.Id] }, ct);
        if (firing.ReleaseRequested) return firing;
        await claimStore.UpsertAsync(claim, ct);
        if (!await ReconcileClaimsAsync(provider, device, ct))
            throw new InvalidOperationException("Claim reconciliation did not complete; will retry next tick.");
        return await CompleteActionAsync(firing, effect.Key, ct);
    }

    private async Task<RuleFiring> ApplyWirelessAsync(IRouterProvider provider, RuleFiring firing,
        SetWireless action, CancellationToken ct)
    {
        if (!Rule.IsSafeWireless(action.NetworkId)) throw new InvalidOperationException("Unsafe wireless network in firing.");
        var id = WirelessNetworkId.Parse(action.NetworkId);
        var current = (await provider.GetWirelessNetworksAsync(ct)).Single(n => n.Id == id);
        if (current.Enabled == action.Enabled)
            return firing with { Error = $"{action.NetworkId} already {(action.Enabled ? "enabled" : "disabled")}; no write needed." };
        if (provider is RuleRouterBudget budget) budget.RequireCapacity();
        if (!firing.WirelessPrior.Any(p => p.NetworkId == action.NetworkId))
            firing = await SaveAsync(firing with { WirelessPrior = [.. firing.WirelessPrior,
                new(action.NetworkId, current.Enabled, action.Enabled)] }, ct);
        else
            firing = await SaveAsync(firing with { WirelessPrior = firing.WirelessPrior.Select(p =>
                p.NetworkId == action.NetworkId ? p with { AppliedEnabled = action.Enabled } : p).ToArray() }, ct);
        if (firing.ReleaseRequested) return firing;
        var applied = await provider.ApplyWirelessAsync(id, new() { Enabled = action.Enabled }, ct);
        if (applied.Enabled != action.Enabled) throw new InvalidOperationException("Router did not confirm the wireless change.");
        return firing;
    }

    private async Task<bool> ReconcileClaimsAsync(IRouterProvider provider, Device device, CancellationToken ct)
    {
        var previous = device.QuotaThrottle;
        try { return await claimEnforcement.ReconcileDeviceAsync(provider, device, ct); }
        catch
        {
            // Claim reconciliation projects intent before the write; a deferred write must not look applied.
            device.QuotaThrottle = previous;
            throw;
        }
    }

    private Task<RuleFiring> CompleteActionAsync(RuleFiring firing, string key, CancellationToken ct) =>
        SaveAsync(firing with { CompletedActionKeys = [.. firing.CompletedActionKeys, key] }, ct);

    private async Task NotifyAsync(RuleFiring firing, string title, string body, CancellationToken ct)
    {
        title = Template(title, firing);
        body = Template(body, firing);
        if (firing.DryRun)
        {
            title = "[Dry-run] " + title;
            body += " Would have performed: " + DescribeActions(firing);
        }
        if (firing.Actions.Any(a => a is Throttle or Block))
        {
            var protectedMacs = new List<MacAddress>();
            foreach (var mac in firing.TargetMacs)
                if (await protectedDevices.IsProtectedAsync(mac, ct)) protectedMacs.Add(mac);
            if (protectedMacs.Count > 0) body += " Protected devices: throttle/block skipped for " + string.Join(", ", protectedMacs) + ".";
        }
        await notifications.DispatchAsync(new(title, body, "/",
            $"rule:{firing.RuleId}:{firing.Subject}:{firing.EpisodeKey}", []),
            firing.DryRun ? NotificationInterruption.QuietHoursEligible : NotificationInterruption.Immediate, ct);
    }

    private static string Template(string text, RuleFiring firing) => Regex.Replace(text, @"\{(rule|device|used|cap)\}", match =>
        match.Groups[1].Value switch
        {
            "rule" => firing.RuleName,
            "device" => firing.DeviceName,
            "used" => firing.UsedBytes.ToString(CultureInfo.InvariantCulture),
            "cap" => firing.CapBytes.ToString(CultureInfo.InvariantCulture),
            _ => match.Value
        });

    private static string DescribeActions(RuleFiring firing) => string.Join(", ", firing.Actions.Select(a => a switch
    {
        Notify => "notify",
        Throttle t => $"throttle {firing.Subject} to {t.Kbps} Kbps",
        Block => $"block {firing.Subject}",
        SetWireless w => $"{(w.Enabled ? "enable" : "disable")} {w.NetworkId}",
        _ => "unknown action"
    }));
}
