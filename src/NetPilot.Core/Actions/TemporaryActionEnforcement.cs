using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Actions;

public sealed class TemporaryActionEnforcement(IActionClaimStore claims, IDeviceStore devices,
    IProtectedDeviceStore protection, ClaimEnforcementService enforcement, UsagePeriodClock clock, TemporaryActionOptions? options = null)
{
    public async Task EvaluateAsync(IRouterProvider provider, CancellationToken ct)
    {
        var requests = (await claims.GetAllAsync(ct)).Where(claim => claim.Source.StartsWith("temporary:", StringComparison.Ordinal));
        var failures = new List<Exception>();
        foreach (var group in requests.GroupBy(claim => claim.Mac))
        {
            try { await ReconcileGroupAsync(provider, group, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { failures.Add(ex); }
        }
        // Each device gets a turn; failures still reach the Worker heartbeat and are retried.
        if (failures.Count > 0) throw new AggregateException("Temporary action evaluation failed.", failures);
    }

    private async Task ReconcileGroupAsync(IRouterProvider provider, IGrouping<MacAddress, ActionClaim> group, CancellationToken ct)
    {
        var device = await devices.FindByMacAsync(group.Key, ct);
        if (device is null) return;
        var protectedDevice = await protection.IsProtectedAsync(group.Key, ct);
        var retired = new List<ActionClaim>();
        foreach (var claim in group)
        {
            if (claim.ExpiresAtUtc <= clock.GetUtcNow() || protectedDevice || (!claim.DryRun && (options?.ForceDryRun ?? true)))
            {
                await claims.UpsertAsync(claim with { Pending = true }, ct);
                retired.Add(claim);
            }
            else if (!claim.DryRun && claim.Pending)
                await claims.UpsertAsync(claim with { Pending = false }, ct);
        }
        // Expired previews never owned router state. Live retirements stay durable until restoration succeeds.
        if (group.Any(claim => !claim.DryRun) && !await enforcement.ReconcileDeviceAsync(provider, device, ct)) return;
        foreach (var claim in retired) await claims.RemoveAsync(claim.Id, ct);
    }
}
