using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Policy;

namespace NetPilot.Core.Enforcement.Claims;

// Router effects are serialized by the Agent loop; Web only edits source data.
public class ClaimEnforcementService(
    IActionClaimStore claimStore,
    IDeviceStore deviceStore,
    IPolicyStore policyStore,
    PolicyReconciliationService reconciliation)
{
    public async Task<ClaimDeviceState> PrepareStateAsync(ClaimDeviceState seed, CancellationToken ct)
    {
        var state = await claimStore.FindDeviceStateAsync(seed.Mac, ct);
        if (state is null)
            state = seed;
        else if (state.PriorLimit is null && seed.PriorLimit is not null)
            state = state with { PriorLimit = seed.PriorLimit };
        await claimStore.UpsertDeviceStateAsync(state, ct);
        return state;
    }

    private async Task<SpeedLimit> CapturePriorLimitAsync(Device device, CancellationToken ct)
    {
        var state = await claimStore.FindDeviceStateAsync(device.Mac, ct);
        if (state?.PriorLimit is { } prior)
            return prior;
        var policy = await policyStore.FindByCategoryAsync(device.CategoryKey, ct);
        var limit = device.Override ?? (policy is { IsUserConfigured: true } ? policy.Limit : null)
            ?? (device.RouterReportedLimit is { } reported
                ? new SpeedLimit(reported.Enabled, reported.DownloadKbps, reported.UploadKbps)
                : throw new InvalidOperationException("Cannot throttle without a speed limit to restore."));
        await PrepareStateAsync(new(device.Mac, limit), ct);
        return limit;
    }

    public async Task<bool> ReconcileDeviceAsync(IRouterProvider provider, Device device, CancellationToken ct)
    {
        var claims = await claimStore.GetForDeviceAsync(device.Mac, ct);
        var effective = EffectiveRestriction.Compute(claims);
        var state = await claimStore.FindDeviceStateAsync(device.Mac, ct) ?? new(device.Mac);
        if (state.UnblockProjectionPending)
            state = await CompleteUnblockProjectionAsync(device, state, ct);
        if (claims.Any(c => c.IsEffective && c.Kind == ClaimKind.Throttle) && state.PriorLimit is null)
            state = state with { PriorLimit = await CapturePriorLimitAsync(device, ct) };
        if (effective.IsBlocked)
            return await ApplyBlockAsync(provider, (device, state), ct);
        if (state.BlockToken is not null)
            state = await ReleaseBlockAsync(provider, (device, state), ct);
        // An existing manual block is never adopted, modified or undone by claims.
        if (device.IsBlocked)
            return true;
        return await ReconcileThrottleAsync(provider, device, (effective.Throttle, state), ct);
    }

    private async Task<bool> ApplyBlockAsync(IRouterProvider provider, (Device Device, ClaimDeviceState State) context, CancellationToken ct)
    {
        var (device, state) = context;
        if (state.BlockToken is null)
        {
            if (device.IsBlocked)
                return true;
            var token = await provider.BlockDeviceAsync(device.Mac, ct);
            state = state with { BlockToken = token };
            // Save the opaque reversal token before any device projection can fail.
            await claimStore.UpsertDeviceStateAsync(state, ct);
        }
        device.IsBlocked = true;
        device.BlockListToken = state.BlockToken;
        await deviceStore.UpsertAsync(device, ct);
        return true;
    }

    private async Task<ClaimDeviceState> ReleaseBlockAsync(IRouterProvider provider, (Device Device, ClaimDeviceState State) context, CancellationToken ct)
    {
        var (device, state) = context;
        await provider.UnblockDeviceAsync(device.Mac, state.BlockToken!, ct);
        state = state with { BlockToken = null, UnblockProjectionPending = true };
        await claimStore.UpsertDeviceStateAsync(state, ct);
        return await CompleteUnblockProjectionAsync(device, state, ct);
    }

    private async Task<ClaimDeviceState> CompleteUnblockProjectionAsync(Device device, ClaimDeviceState state, CancellationToken ct)
    {
        device.IsBlocked = false;
        device.BlockListToken = null;
        await deviceStore.UpsertAsync(device, ct);
        state = state with { UnblockProjectionPending = false };
        await claimStore.UpsertDeviceStateAsync(state, ct);
        return state;
    }

    private async Task<bool> ReconcileThrottleAsync(IRouterProvider provider, Device device,
        (SpeedLimit? Throttle, ClaimDeviceState State) desired, CancellationToken ct)
    {
        if (desired.Throttle is { } throttle)
            return await ApplyThrottleAsync(provider, device, throttle, ct);
        if (desired.State.PriorLimit is null)
            return true;
        if (!await reconciliation.RestoreQuotaLimitAsync(provider, device, desired.State.PriorLimit, ct))
            return false;
        device.QuotaThrottle = null;
        await deviceStore.UpsertAsync(device, ct);
        await claimStore.UpsertDeviceStateAsync(desired.State with { PriorLimit = null }, ct);
        return true;
    }

    private async Task<bool> ApplyThrottleAsync(IRouterProvider provider, Device device, SpeedLimit throttle, CancellationToken ct)
    {
        var previous = device.QuotaThrottle;
        if (previous == throttle)
            return true;
        device.QuotaThrottle = throttle;
        if (!await reconciliation.ApplyDevicePolicyAsync(provider, device, ct))
        {
            device.QuotaThrottle = previous;
            return false;
        }
        await deviceStore.UpsertAsync(device, ct);
        return true;
    }
}
