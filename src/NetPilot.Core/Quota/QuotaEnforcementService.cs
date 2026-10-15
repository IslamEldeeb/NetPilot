using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
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
    IActionClaimStore claimStore,
    ClaimEnforcementService claimEnforcement,
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
            // Keep the existing daily/monthly sharing semantics, including after policy edits.
            state = state with { ActionKind = existing.ActionKind, ActionAppliedAtUtc = existing.ActionAppliedAtUtc,
                PriorLimit = existing.PriorLimit, BlockToken = existing.BlockToken, AppliedThrottleKbps = existing.AppliedThrottleKbps };
        }
        else
        {
            var ownership = await claimStore.FindDeviceStateAsync(state.Mac, ct);
            if (context.Device.IsBlocked && ownership?.BlockToken is null)
                return;
            if (context.Policy.Action == QuotaAction.Throttle)
            {
                var prepared = await claimEnforcement.PrepareStateAsync(new(state.Mac,
                    state.PriorLimit ?? ownership?.PriorLimit ?? await PriorLimitAsync(context, ct)), ct);
                state = state with { PriorLimit = prepared.PriorLimit, AppliedThrottleKbps = context.Policy.ThrottleKbps };
            }
            // Failed writes leave the claim durable but the quota action unactioned, as before.
            state = await SaveAgentStateAsync(state, ct);
        }
        var kind = existing?.ActionKind ?? (context.Policy.Action == QuotaAction.Block ? QuotaActionKind.Block : QuotaActionKind.Throttle);
        var otherKind = kind == QuotaActionKind.Block ? QuotaActionKind.Throttle : QuotaActionKind.Block;
        await claimStore.RemoveAsync(ClaimFor(state with { ActionKind = otherKind }).Id, ct);
        await claimStore.UpsertAsync(ClaimFor(state with { ActionKind = kind }), ct);
        var wasBlocked = context.Device.IsBlocked;
        if (!await claimEnforcement.ReconcileDeviceAsync(provider, context.Device, ct))
            return;
        var applied = await claimStore.FindDeviceStateAsync(state.Mac, ct);
        state = state with { ActionKind = kind, ActionAppliedAtUtc = existing?.ActionAppliedAtUtc ?? clock.GetUtcNow(),
            BlockToken = kind == QuotaActionKind.Block ? applied?.BlockToken : null };
        state = await SaveAgentStateAsync(state, ct);
        if (!wasBlocked && context.Device.IsBlocked)
            await activityLog.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.DeviceBlocked, state.Mac,
                $"{DisplayName(context.Device)} blocked by quota"), ct);
        await PublishAsync(CreateEvent(QuotaEventKind.ActionApplied, context, state), ct);
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
        foreach (var group in (await StatesWithClaimsAsync(ct)).GroupBy(s => s.Mac))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await RevertDeviceActionsAsync(provider, group.ToArray(), policies, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await LogFailureAsync(group.Key, $"Quota revert failed: {ex.Message}", ct);
            }
        }
    }

    private async Task RevertDeviceActionsAsync(IRouterProvider provider, QuotaPeriodState[] states,
        IReadOnlyList<QuotaPolicy> policies, CancellationToken ct)
    {
        var device = await deviceStore.FindByMacAsync(states[0].Mac, ct)
            ?? throw new InvalidOperationException("Quota action device is missing from the device store.");
        var usage = await usageStore.FindStateAsync(device.Mac, ct);
        var resolution = QuotaResolver.Resolve(device, policies);
        var refreshed = states.Select(s => RefreshPeriodUsage(s, usage, resolution)).ToArray();
        var pendingAllowed = resolution is { Kind: QuotaResolutionKind.Policy, MatchedPolicy: { DryRun: false } policy }
            && policy.Action != QuotaAction.NotifyOnly && !await protectedDevices.IsProtectedAsync(device.Mac, ct);
        var expired = refreshed.Where(s => ShouldRevert(s, usage)
            || (s.ActionKind == QuotaActionKind.None && !pendingAllowed)).ToArray();
        // Pending applies still pass through the current dry-run/protected checks below.
        if (expired.Length == 0 && refreshed.All(s => s.ActionKind == QuotaActionKind.None))
            return;
        await RestoreActionClaimsAsync(device, refreshed, ct);
        foreach (var state in expired)
        {
            // An interrupted apply has no ActionKind yet; release either possible claim ID.
            await claimStore.RemoveAsync(ClaimFor(state with { ActionKind = QuotaActionKind.Throttle }).Id, ct);
            await claimStore.RemoveAsync(ClaimFor(state with { ActionKind = QuotaActionKind.Block }).Id, ct);
        }
        var wasBlocked = device.IsBlocked;
        if (!await claimEnforcement.ReconcileDeviceAsync(provider, device, ct))
            return;
        if (wasBlocked && !device.IsBlocked)
            await activityLog.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.DeviceUnblocked, device.Mac,
                $"{DisplayName(device)} unblocked after quota reverted"), ct);
        foreach (var state in expired)
            await CompleteRevertAsync(device, state, ct);
    }

    private async Task CompleteRevertAsync(Device device, QuotaPeriodState state, CancellationToken ct)
    {
        await stateStore.UpdateAsync(state.Id, current => current is null ? null : current with
        {
            ActionKind = QuotaActionKind.None, PriorLimit = null, BlockToken = null, AppliedThrottleKbps = null,
            ReleaseRequested = state.ReleaseRequested ? false : current.ReleaseRequested,
            ActionSuppressedForPeriod = current.ActionSuppressedForPeriod || state.ReleaseRequested
        }, ct);
        if (state.ActionKind != QuotaActionKind.None)
            await PublishAsync(RevertedEvent(device, state), ct);
    }

    private async Task<IReadOnlyList<QuotaPeriodState>> StatesWithClaimsAsync(CancellationToken ct)
    {
        var states = (await stateStore.ListWithActiveActionAsync(ct)).ToDictionary(s => s.Id);
        // Failed applies have a durable claim but no ActionKind yet; rollover must release them too.
        foreach (var claim in (await claimStore.GetAllAsync(ct)).Where(c => c.Source.StartsWith("quota:", StringComparison.Ordinal)))
        {
            var parts = claim.Source.Split(':', 3);
            if (parts.Length != 3 || parts[1] is not ("day" or "month"))
                continue;
            var id = QuotaPeriodState.Key(claim.Mac, parts[1] == "day" ? QuotaPeriodKind.Day : QuotaPeriodKind.Month, parts[2]);
            if (!states.ContainsKey(id) && await stateStore.FindAsync(id, ct) is { } state)
                states[id] = state;
        }
        return states.Values.ToArray();
    }

    private async Task RestoreActionClaimsAsync(Device device, QuotaPeriodState[] states, CancellationToken ct)
    {
        // Import old reversal metadata only when no claim record exists. A completed unblock must
        // never re-adopt an old quota token while its bookkeeping is still waiting to be cleared.
        var active = states.FirstOrDefault(s => s.ActionKind != QuotaActionKind.None);
        if (active is not null && await claimStore.FindDeviceStateAsync(device.Mac, ct) is null)
        {
            if (active.ActionKind == QuotaActionKind.Block && active.BlockToken is null)
                throw new InvalidOperationException("Quota block token is missing; cannot safely repair claims.");
            await claimEnforcement.PrepareStateAsync(new(device.Mac, active.PriorLimit, active.BlockToken), ct);
        }
        foreach (var state in states.Where(s => s.ActionKind != QuotaActionKind.None))
            await claimStore.UpsertAsync(ClaimFor(state), ct);
    }

    private ActionClaim ClaimFor(QuotaPeriodState state) =>
        new(state.Mac, state.ActionKind == QuotaActionKind.Block ? ClaimKind.Block : ClaimKind.Throttle,
            $"quota:{(state.PeriodKind == QuotaPeriodKind.Day ? "day" : "month")}:{state.PeriodKey}",
            state.ActionKind == QuotaActionKind.Block ? null : state.AppliedThrottleKbps,
            state.ActionAppliedAtUtc ?? clock.GetUtcNow(), "Quota exceeded");

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
