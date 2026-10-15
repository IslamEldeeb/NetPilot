using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Quota;

public class QuotaEnforcementService(
    IDeviceStore deviceStore,
    IUsageStore usageStore,
    IQuotaStore quotaStore,
    IQuotaStateStore stateStore,
    IProtectedDeviceStore protectedDevices,
    IPolicyStore policyStore,
    PolicyReconciliationService reconciliation,
    IActivityLogStore activityLog,
    IEnumerable<IQuotaEventSink> eventSinks,
    UsagePeriodClock clock)
{
    public async Task EvaluateAsync(IRouterProvider provider, IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        var policies = await quotaStore.GetAllAsync(ct);
        await RevertExpiredActionsAsync(provider, policies, ct);

        // Blocked devices can disappear from router snapshots; persisted usage remains authoritative.
        foreach (var device in await deviceStore.GetAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var resolution = QuotaResolver.Resolve(device, policies);
                var usage = await usageStore.FindStateAsync(device.Mac, ct);
                if (resolution.Kind != QuotaResolutionKind.Policy || usage is null)
                    continue;
                var reported = snapshots.FirstOrDefault(s => new MacAddress(s.MacAddress) == device.Mac)?.CurrentLimit
                    ?? device.RouterReportedLimit;
                var context = new DeviceQuota(device, resolution.MatchedPolicy!, usage, reported);
                await EvaluateDeviceAsync(provider, context, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await LogFailureAsync(device.Mac, $"Quota evaluation failed: {ex.Message}", ct);
            }
        }

        await stateStore.PruneAsync(clock.GetUtcNow().AddDays(-62), ct);
    }

    // Web owns allowances, consumed token hashes and release requests. Preserve its latest transaction
    // even when the Agent's router operation or event fan-out started from an older snapshot.
    private async Task<QuotaPeriodState> SaveAgentStateAsync(QuotaPeriodState proposed, CancellationToken ct) =>
        (await stateStore.UpdateAsync(proposed.Id, current => current is null ? proposed : proposed with
        {
            ExtraAllowanceBytes = current.ExtraAllowanceBytes,
            UsedActionTokenHashes = current.UsedActionTokenHashes,
            ReleaseRequested = current.ReleaseRequested,
            ActionSuppressedForPeriod = current.ActionSuppressedForPeriod
        }, ct))!;

    private sealed record DeviceQuota(Device Device, QuotaPolicy Policy, DeviceUsageState Usage, SpeedLimitState? ReportedLimit);

    private async Task EvaluateDeviceAsync(IRouterProvider provider, DeviceQuota context, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var periods = new[]
        {
            (Kind: QuotaPeriodKind.Day, Key: clock.DayKey(now), UsageKey: context.Usage.CurrentDayKey,
                Used: context.Usage.CurrentDayBytes, Cap: context.Policy.DailyBytes),
            (Kind: QuotaPeriodKind.Month, Key: clock.MonthKey(now), UsageKey: context.Usage.CurrentMonthKey,
                Used: context.Usage.CurrentMonthBytes, Cap: context.Policy.MonthlyBytes)
        };
        foreach (var period in periods)
        {
            if (period.Cap is not { } cap || period.Key != period.UsageKey)
                continue;
            var state = await stateStore.FindAsync(QuotaPeriodState.Key(context.Device.Mac, period.Kind, period.Key), ct)
                ?? new QuotaPeriodState { Mac = context.Device.Mac, PeriodKind = period.Kind, PeriodKey = period.Key };
            state = state with { CapBytes = cap, UsedBytes = period.Used };
            state = await SaveAgentStateAsync(state, ct);
            await EvaluatePeriodAsync(provider, context, state, ct);
        }
    }

    private async Task EvaluatePeriodAsync(IRouterProvider provider, DeviceQuota context, QuotaPeriodState state, CancellationToken ct)
    {
        state = (await stateStore.UpdateAsync(state.Id, current => current is { ReleaseRequested: true, ActionKind: QuotaActionKind.None }
            ? current with { ReleaseRequested = false, ActionSuppressedForPeriod = true } : current, ct)) ?? state;
        var crossed = context.Policy.WarnPercents.Where(p => state.Percent >= p && !state.WarnedPercents.Contains(p)).ToArray();
        if (crossed.Length > 0)
        {
            state = state with { WarnedPercents = state.WarnedPercents.Concat(crossed).Distinct().Order().ToArray() };
            state = await SaveAgentStateAsync(state, ct);
            // One warning carries the current usage; every crossed threshold is acknowledged durably.
            await PublishAsync(CreateEvent(QuotaEventKind.Warning, context, state) with { WarnPercent = crossed.Max() }, ct);
        }
        if (state.Percent < 100)
            return;
        if (!state.ExceededNotified)
        {
            state = state with { ExceededNotified = true };
            state = await SaveAgentStateAsync(state, ct);
            await PublishAsync(CreateEvent(QuotaEventKind.Exceeded, context, state), ct);
        }
        await ApplyActionAsync(provider, context, state, ct);
    }

    private async Task ApplyActionAsync(IRouterProvider provider, DeviceQuota context, QuotaPeriodState state, CancellationToken ct)
    {
        state = await stateStore.FindAsync(state.Id, ct) ?? state;
        if (state.ReleaseRequested || state.ActionSuppressedForPeriod || state.Percent < 100 ||
            context.Policy.Action == QuotaAction.NotifyOnly || state.ActionKind != QuotaActionKind.None)
            return;
        if (await protectedDevices.IsProtectedAsync(context.Device.Mac, ct))
        {
            if (!state.ActionSkippedProtected)
            {
                state = state with { ActionSkippedProtected = true };
                state = await SaveAgentStateAsync(state, ct);
                await PublishAsync(CreateEvent(QuotaEventKind.ActionSkippedProtected, context, state), ct);
            }
            return;
        }
        if (context.Policy.DryRun)
        {
            if (!state.DryRunWouldHaveActed)
            {
                state = state with { DryRunWouldHaveActed = true };
                state = await SaveAgentStateAsync(state, ct);
                await PublishAsync(CreateEvent(QuotaEventKind.DryRun, context, state), ct);
            }
            return;
        }
        await ApplyLiveActionAsync(provider, context, state, ct);
    }

    private async Task ApplyLiveActionAsync(IRouterProvider provider, DeviceQuota context, QuotaPeriodState state, CancellationToken ct)
    {
        var existing = (await stateStore.ListWithActiveActionAsync(ct)).FirstOrDefault(s => s.Mac == state.Mac);
        if (existing is not null)
        {
            state = state with { ActionKind = existing.ActionKind, ActionAppliedAtUtc = existing.ActionAppliedAtUtc,
                PriorLimit = existing.PriorLimit, BlockToken = existing.BlockToken, AppliedThrottleKbps = existing.AppliedThrottleKbps };
        }
        else
        {
            // A manual block belongs to the user; never take ownership and unblock it at rollover.
            if (context.Device.IsBlocked)
                return;
            state = context.Policy.Action == QuotaAction.Block
                ? await BlockAsync(provider, context, state, ct)
                : await ThrottleAsync(provider, context, state, ct);
            if (state.ActionKind == QuotaActionKind.None)
                return;
        }

        // Save the reversal token before the device projection or any event sink can fail.
        state = await SaveAgentStateAsync(state, ct);
        await deviceStore.UpsertAsync(context.Device, ct);
        await PublishAsync(CreateEvent(QuotaEventKind.ActionApplied, context, state), ct);
    }

    private async Task<QuotaPeriodState> BlockAsync(IRouterProvider provider, DeviceQuota context, QuotaPeriodState state, CancellationToken ct)
    {
        var token = await provider.BlockDeviceAsync(state.Mac, ct);
        state = state with { ActionKind = QuotaActionKind.Block, BlockToken = token, ActionAppliedAtUtc = clock.GetUtcNow() };
        state = await SaveAgentStateAsync(state, ct);
        context.Device.IsBlocked = true;
        context.Device.BlockListToken = token;
        await deviceStore.UpsertAsync(context.Device, ct);
        await activityLog.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.DeviceBlocked, state.Mac,
            $"{DisplayName(context.Device)} blocked by quota"), ct);
        return state;
    }

    private async Task<QuotaPeriodState> ThrottleAsync(IRouterProvider provider, DeviceQuota context, QuotaPeriodState state, CancellationToken ct)
    {
        var prior = state.PriorLimit ?? await PriorLimitAsync(context, ct);
        // Preserve the original limit even if the process stops immediately after the router write.
        state = state with { PriorLimit = prior, AppliedThrottleKbps = context.Policy.ThrottleKbps };
        state = await SaveAgentStateAsync(state, ct);
        var previousThrottle = context.Device.QuotaThrottle;
        context.Device.QuotaThrottle = new SpeedLimit(true, context.Policy.ThrottleKbps, context.Policy.ThrottleKbps);
        if (!await reconciliation.ApplyDevicePolicyAsync(provider, context.Device, ct))
        {
            context.Device.QuotaThrottle = previousThrottle;
            return state;
        }
        return state with { ActionKind = QuotaActionKind.Throttle, PriorLimit = prior, ActionAppliedAtUtc = clock.GetUtcNow(),
            AppliedThrottleKbps = context.Policy.ThrottleKbps };
    }

    private async Task<SpeedLimit> PriorLimitAsync(DeviceQuota context, CancellationToken ct)
    {
        var policy = await policyStore.FindByCategoryAsync(context.Device.CategoryKey, ct);
        return context.Device.Override ?? (policy is { IsUserConfigured: true } ? policy.Limit : null)
            ?? (context.ReportedLimit is { } reported
                ? new SpeedLimit(reported.Enabled, reported.DownloadKbps, reported.UploadKbps)
                : throw new InvalidOperationException("Cannot throttle without a speed limit to restore."));
    }

    private async Task RevertExpiredActionsAsync(IRouterProvider provider, IReadOnlyList<QuotaPolicy> policies, CancellationToken ct)
    {
        foreach (var group in (await stateStore.ListWithActiveActionAsync(ct)).GroupBy(s => s.Mac))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var device = await deviceStore.FindByMacAsync(group.Key, ct)
                    ?? throw new InvalidOperationException("Quota action device is missing from the device store.");
                await RestoreActionProjectionAsync(device, group.First(), ct);
                var usage = await usageStore.FindStateAsync(device.Mac, ct);
                var resolution = QuotaResolver.Resolve(device, policies);
                var expired = group.Select(s => RefreshPeriodUsage(s, usage, resolution))
                    .Where(s => ShouldRevert(s, usage)).ToArray();
                if (expired.Length == 0)
                    continue;
                // Daily and monthly rows are claims on one action, not independent router writes.
                if (expired.Length == group.Count() && !await UndoActionAsync(provider, device, expired[0], ct))
                    continue;
                foreach (var state in expired)
                {
                    await stateStore.UpdateAsync(state.Id, current => current is null ? null : current with
                    {
                        ActionKind = QuotaActionKind.None, PriorLimit = null, BlockToken = null, AppliedThrottleKbps = null,
                        ReleaseRequested = state.ReleaseRequested ? false : current.ReleaseRequested,
                        ActionSuppressedForPeriod = current.ActionSuppressedForPeriod || state.ReleaseRequested
                    }, ct);
                    await PublishAsync(RevertedEvent(device, state), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await LogFailureAsync(group.Key, $"Quota revert failed: {ex.Message}", ct);
            }
        }
    }

    private async Task RestoreActionProjectionAsync(Device device, QuotaPeriodState state, CancellationToken ct)
    {
        // A restart can occur after saving the action but before saving the device projection.
        if (state.ActionKind == QuotaActionKind.Block && (!device.IsBlocked || device.BlockListToken != state.BlockToken))
        {
            device.IsBlocked = true;
            device.BlockListToken = state.BlockToken;
            await deviceStore.UpsertAsync(device, ct);
        }
        else if (state.ActionKind == QuotaActionKind.Throttle && state.AppliedThrottleKbps is { } kbps
            && device.QuotaThrottle != new SpeedLimit(true, kbps, kbps))
        {
            device.QuotaThrottle = new SpeedLimit(true, kbps, kbps);
            await deviceStore.UpsertAsync(device, ct);
        }
    }

    private static QuotaPeriodState RefreshPeriodUsage(QuotaPeriodState state, DeviceUsageState? usage, QuotaResolution resolution)
    {
        if (usage is null)
            return state;
        var usageKey = state.PeriodKind == QuotaPeriodKind.Day ? usage.CurrentDayKey : usage.CurrentMonthKey;
        if (usageKey != state.PeriodKey)
            return state;
        var used = state.PeriodKind == QuotaPeriodKind.Day ? usage.CurrentDayBytes : usage.CurrentMonthBytes;
        var cap = state.PeriodKind == QuotaPeriodKind.Day ? resolution.MatchedPolicy?.DailyBytes : resolution.MatchedPolicy?.MonthlyBytes;
        return state with { UsedBytes = used, CapBytes = cap ?? state.CapBytes };
    }

    private bool ShouldRevert(QuotaPeriodState state, DeviceUsageState? usage)
    {
        if (state.ReleaseRequested) return true;
        var currentKey = state.PeriodKind == QuotaPeriodKind.Day ? clock.TodayKey() : clock.ThisMonthKey();
        if (state.PeriodKey != currentKey)
            return true;
        if (usage is null)
            return false;
        var usageKey = state.PeriodKind == QuotaPeriodKind.Day ? usage.CurrentDayKey : usage.CurrentMonthKey;
        return usageKey == currentKey && state.UsedBytes < state.EffectiveCapBytes;
    }

    private async Task<bool> UndoActionAsync(IRouterProvider provider, Device device, QuotaPeriodState state, CancellationToken ct)
    {
        if (state.ActionKind == QuotaActionKind.Block)
        {
            var token = state.BlockToken ?? throw new InvalidOperationException("Quota block token is missing; cannot safely unblock.");
            await provider.UnblockDeviceAsync(device.Mac, token, ct);
            device.IsBlocked = false;
            device.BlockListToken = null;
            await activityLog.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.DeviceUnblocked, device.Mac,
                $"{DisplayName(device)} unblocked after quota reverted"), ct);
        }
        else
        {
            if (!await reconciliation.RestoreQuotaLimitAsync(provider, device, state.PriorLimit, ct))
                return false;
            device.QuotaThrottle = null;
        }
        await deviceStore.UpsertAsync(device, ct);
        return true;
    }

    private QuotaEvent CreateEvent(QuotaEventKind kind, DeviceQuota context, QuotaPeriodState state) =>
        new(kind, state.Mac, DisplayName(context.Device), state.UsedBytes, state.EffectiveCapBytes, state.Percent,
            state.PeriodKind, state.PeriodKey, kind == QuotaEventKind.ActionApplied ? AppliedAction(state) : context.Policy.Action,
            context.Policy.DryRun, clock.GetUtcNow(), state.AppliedThrottleKbps ?? context.Policy.ThrottleKbps);

    private QuotaEvent RevertedEvent(Device device, QuotaPeriodState state) =>
        new(QuotaEventKind.Reverted, state.Mac, DisplayName(device), state.UsedBytes, state.EffectiveCapBytes, state.Percent,
            state.PeriodKind, state.PeriodKey, AppliedAction(state),
            false, clock.GetUtcNow());

    private static QuotaAction AppliedAction(QuotaPeriodState state) =>
        state.ActionKind == QuotaActionKind.Block ? QuotaAction.Block : QuotaAction.Throttle;

    private async Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct)
    {
        foreach (var sink in eventSinks)
        {
            try
            {
                await sink.PublishAsync(quotaEvent, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await LogFailureAsync(quotaEvent.Mac, $"Quota event sink failed: {ex.Message}", ct);
            }
        }
    }

    private Task LogFailureAsync(MacAddress mac, string message, CancellationToken ct) =>
        activityLog.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.WriteFailed, mac, message), ct);

    private static string DisplayName(Device device) =>
        string.IsNullOrWhiteSpace(device.FriendlyName) ? device.Hostname : device.FriendlyName;
}
