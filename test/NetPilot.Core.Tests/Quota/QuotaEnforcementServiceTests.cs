using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Quota;

public class QuotaEnforcementServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task WarningFiresAtThreshold_OnceAcrossTicksAndRestart()
    {
        var h = await Harness.CreateAsync();
        h.Usage.CurrentDayBytes = 799;
        await h.EvaluateAsync();
        Assert.Empty(h.Events.Events);

        h.Usage.CurrentDayBytes = 800;
        await h.EvaluateAsync();
        h.Usage.CurrentDayBytes = 950;
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        var warning = Assert.Single(h.Events.Events);
        Assert.Equal(QuotaEventKind.Warning, warning.Kind);
        Assert.Equal(80, warning.WarnPercent);
        Assert.Equal(800, warning.UsedBytes);
        Assert.Equal(1000, warning.CapBytes);
        Assert.Equal(80, warning.Percent);
        Assert.Equal("My phone", warning.DeviceDisplayName);
        Assert.Equal(h.Time.UtcNow, warning.AtUtc);
        Assert.Equal(new[] { 80 }, (await h.StateAsync())!.WarnedPercents);
        Assert.Single(h.Log.Entries, e => e.Type == ActivityEventType.QuotaWarning);
    }

    [Fact]
    public async Task JumpAcrossSeveralThresholds_EmitsHighestAndAcknowledgesAll()
    {
        var h = await Harness.CreateAsync(new() { Id = "default", DailyBytes = 1000, WarnPercents = [50, 80, 90] });
        h.Usage.CurrentDayBytes = 950;
        await h.EvaluateAsync();
        h.Usage.CurrentDayBytes = 999;
        await h.EvaluateAsync();

        Assert.Equal(90, Assert.Single(h.Events.Events).WarnPercent);
        Assert.Equal(new[] { 50, 80, 90 }, (await h.StateAsync())!.WarnedPercents);
    }

    [Fact]
    public async Task NotifyOnly_ExceedsExactlyAtCap_OnceWithoutEnforcement()
    {
        var h = await Harness.CreateAsync();
        h.Usage.CurrentDayBytes = 999;
        await h.EvaluateAsync();
        Assert.DoesNotContain(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        await h.EvaluateAsync();

        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        Assert.True((await h.StateAsync())!.ExceededNotified);
        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.BlockedMacs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThrottleBeatsOverrideAndCategory_WritesOnce_ThenRestoresLatestOverride(bool configured)
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Throttle), configured);
        var originalOverride = new SpeedLimit(true, 4000, 2000);
        h.Device.Override = originalOverride;
        h.Usage.CurrentDayBytes = 1000;

        await h.EvaluateAsync();
        Assert.Equal(new SpeedLimit(true, 512, 512), h.Device.QuotaThrottle);
        Assert.Equal(originalOverride, h.Device.Override);
        Assert.Equal(originalOverride, (await h.StateAsync())!.PriorLimit);
        await h.ReconcileAsync();
        await h.ReconcileAsync();
        Assert.Equal(new SpeedLimit(true, 512, 512), Assert.Single(h.Provider.AppliedLimits).Limit);

        h.Device.Override = new SpeedLimit(true, 3000, 1500);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.EvaluateAsync();
        await h.ReconcileAsync();
        await h.ReconcileAsync();

        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(new SpeedLimit(true, 3000, 1500), h.Device.Override);
        Assert.Equal(2, h.Provider.AppliedLimits.Count);
        Assert.Equal(h.Device.Override, h.Provider.AppliedLimits[1].Limit);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Reverted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThrottleWithoutOverride_RestoresConfiguredPolicyOrCapturedRouterLimit(bool configured)
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Throttle), configured);
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        Assert.Equal(Harness.Normal, (await h.StateAsync())!.PriorLimit);
        Assert.Equal(new SpeedLimit(true, 512, 512), Assert.Single(h.Provider.AppliedLimits).Limit);

        var restored = new SpeedLimit(true, 7000, 2500);
        if (configured)
            await h.Policies.UpsertAsync(new("mobile", restored, 2, true), Ct);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.EvaluateAsync();
        await h.ReconcileAsync();

        Assert.Null(h.Device.Override);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(configured ? restored : Harness.Normal, h.Provider.AppliedLimits.Last().Limit);
        Assert.Equal(2, h.Provider.AppliedLimits.Count);
    }

    [Fact]
    public async Task BlockPersistsToken_AndRevertsEvenWhenDeviceDisappearsFromSnapshots()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.Single(h.Provider.BlockedMacs);
        var state = (await h.StateAsync())!;
        Assert.True(h.Device.IsBlocked);
        Assert.Equal($"fake-token-{h.Device.Mac}", state.BlockToken);
        Assert.Equal(state.BlockToken, h.Device.BlockListToken);
        Assert.Single(h.Log.Entries, e => e.Type == ActivityEventType.DeviceBlocked);
        h.Provider.Devices.Clear();
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        await h.EvaluateAsync();

        Assert.Equal(((string)h.Device.Mac, state.BlockToken), Assert.Single(h.Provider.UnblockRequests));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.BlockListToken);
        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.Single(h.Log.Entries, e => e.Type == ActivityEventType.DeviceUnblocked);
    }

    [Fact]
    public async Task ManuallyBlockedDevice_IsNeverClaimedOrAutomaticallyUnblocked()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Device.IsBlocked = true;
        h.Device.BlockListToken = "manual-token";
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.EvaluateAsync();

        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Provider.UnblockRequests);
        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.True(h.Device.IsBlocked);
        Assert.Equal("manual-token", h.Device.BlockListToken);
    }

    [Theory]
    [InlineData(QuotaAction.Throttle)]
    [InlineData(QuotaAction.Block)]
    public async Task DryRun_RecordsWouldHaveActedOnce_WithoutDeviceOrRouterMutation(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action) with { DryRun = true });
        var originalFingerprint = h.Device.LastAppliedFingerprint;
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Null(h.Device.Override);
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.BlockListToken);
        Assert.Equal(originalFingerprint, h.Device.LastAppliedFingerprint);
        Assert.True((await h.StateAsync())!.DryRunWouldHaveActed);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Warning);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        Assert.True(Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.DryRun).DryRun);
        Assert.Contains(h.Log.Entries, e => e.Type == ActivityEventType.QuotaDryRun && e.Message.Contains(
            action == QuotaAction.Throttle ? "would have throttled My phone to 512 Kbps" : "would have blocked My phone"));
    }

    [Theory]
    [InlineData(QuotaAction.Throttle)]
    [InlineData(QuotaAction.Block)]
    public async Task ProtectedDevice_LogsSkippedActionOnce_AndStillNotifies(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action));
        await h.Protected.AddAsync(h.Device.Mac, Ct);
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        await h.EvaluateAsync();

        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.False(h.Device.IsBlocked);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.ActionSkippedProtected);
        Assert.Contains(h.Log.Entries, e => e.Type == ActivityEventType.QuotaActionSkippedProtected && e.Message.Contains("protected"));
    }

    [Theory]
    [InlineData("exempt")]
    [InlineData("not-configured")]
    [InlineData("no-caps")]
    [InlineData("no-usage")]
    public async Task DevicesWithoutAnApplicableCap_AreSkipped(string reason)
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Usage.CurrentDayBytes = 1000;
        if (reason == "not-configured")
            await h.Quotas.DeleteAsync("default", Ct);
        if (reason == "exempt")
            await h.Quotas.UpsertAsync(Live(QuotaAction.Block) with { Exempt = true }, Ct);
        if (reason == "no-caps")
            await h.Quotas.UpsertAsync(Live(QuotaAction.Block) with { DailyBytes = null }, Ct);
        var service = reason == "no-usage" ? h.NewService(new InMemoryUsageStore()) : h.Service;

        await service.EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.Empty(h.Events.Events);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Null(await h.StateAsync());
    }

    [Fact]
    public async Task DailyAndMonthlyWarningsAndExceeds_AreIndependent()
    {
        var h = await Harness.CreateAsync(new() { Id = "default", DailyBytes = 1000, MonthlyBytes = 2000 });
        h.Usage.CurrentDayBytes = 1000;
        h.Usage.CurrentMonthBytes = 1600;
        await h.EvaluateAsync();
        Assert.Equal(2, h.Events.Events.Count(e => e.Kind == QuotaEventKind.Warning));
        Assert.Equal(QuotaPeriodKind.Day, Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded).PeriodKind);
        h.Usage.CurrentMonthBytes = 2000;
        await h.EvaluateAsync();
        await h.EvaluateAsync();
        Assert.Equal(2, h.Events.Events.Count(e => e.Kind == QuotaEventKind.Exceeded));
        Assert.True((await h.StateAsync(QuotaPeriodKind.Month))!.ExceededNotified);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task SharedDailyAndMonthlyAction_PersistsAtMidnight_AndRevertsAtMonthBoundary(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action) with { MonthlyBytes = 1000 });
        h.Usage.CurrentDayBytes = h.Usage.CurrentMonthBytes = 1000;
        await h.EvaluateAsync();
        Assert.Equal(2, (await h.States.ListWithActiveActionAsync(Ct)).Count);
        Assert.Equal(1, h.Provider.BlockedMacs.Count + h.Provider.AppliedLimits.Count);

        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1); // Cairo midnight, still July.
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        Assert.Equal(QuotaPeriodKind.Month, Assert.Single(await h.States.ListWithActiveActionAsync(Ct)).PeriodKind);
        Assert.Empty(h.Provider.UnblockRequests);
        Assert.Equal(1, h.Provider.BlockedMacs.Count + h.Provider.AppliedLimits.Count);
        Assert.Equal(action == QuotaAction.Throttle, h.Device.QuotaThrottle is not null);
        h.Time.UtcNow = new(2026, 7, 31, 21, 0, 0, TimeSpan.Zero);
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        await h.EvaluateAsync();

        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(action == QuotaAction.Block ? 1 : 0, h.Provider.UnblockRequests.Count);
        Assert.Equal(action == QuotaAction.Throttle ? 2 : 0, h.Provider.AppliedLimits.Count);
        Assert.Equal(2, h.Events.Events.Count(e => e.Kind == QuotaEventKind.Reverted));
    }

    [Theory]
    [InlineData("2026-07-25T21:30:00Z", "2026-07-26T00:00:00Z", "2026-07-26T21:00:00Z")]
    [InlineData("2026-01-25T22:30:00Z", "2026-01-26T00:00:00Z", "2026-01-26T22:00:00Z")]
    public async Task DailyAction_RevertsAtCairoMidnight_NotUtcMidnight(string started, string utcMidnight, string cairoMidnight)
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Time.UtcNow = DateTimeOffset.Parse(started);
        h.Usage.CurrentDayKey = h.Clock.TodayKey();
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        h.Time.UtcNow = DateTimeOffset.Parse(utcMidnight);
        await h.EvaluateAsync();
        Assert.True(h.Device.IsBlocked);
        Assert.Empty(h.Provider.UnblockRequests);

        h.Time.UtcNow = DateTimeOffset.Parse(cairoMidnight);
        await h.EvaluateAsync();
        Assert.False(h.Device.IsBlocked);
        Assert.Single(h.Provider.UnblockRequests);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task ExtraAllowance_ReleasesOnlyItsPeriod_ThenRestoresAfterLastClaimEnds(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action) with { MonthlyBytes = 1000 });
        h.Usage.CurrentDayBytes = h.Usage.CurrentMonthBytes = 1000;
        await h.EvaluateAsync();
        var day = (await h.StateAsync())!;
        await h.States.UpsertAsync(day with { ExtraAllowanceBytes = 1000 }, Ct);
        await h.EvaluateAsync();
        Assert.Equal(QuotaPeriodKind.Month, Assert.Single(await h.States.ListWithActiveActionAsync(Ct)).PeriodKind);
        Assert.Empty(h.Provider.UnblockRequests);
        Assert.Equal(1, h.Provider.AppliedLimits.Count + h.Provider.BlockedMacs.Count);

        var month = (await h.StateAsync(QuotaPeriodKind.Month))!;
        await h.States.UpsertAsync(month with { ExtraAllowanceBytes = 1000 }, Ct);
        await h.EvaluateAsync();
        await h.EvaluateAsync();
        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(action == QuotaAction.Block ? 1 : 0, h.Provider.UnblockRequests.Count);
        Assert.Equal(action == QuotaAction.Throttle ? 2 : 0, h.Provider.AppliedLimits.Count);
    }

    [Fact]
    public async Task ExtraAllowance_IsUsedForThresholdsAndCanBeExceededAgainWithoutRenotifying()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        var state = new QuotaPeriodState { Mac = h.Device.Mac, PeriodKind = QuotaPeriodKind.Day,
            PeriodKey = h.Clock.TodayKey(), ExtraAllowanceBytes = 1000 };
        await h.States.UpsertAsync(state, Ct);
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        Assert.Empty(h.Events.Events);
        h.Usage.CurrentDayBytes = 2000;
        await h.EvaluateAsync();
        Assert.Single(h.Provider.BlockedMacs);
        state = (await h.StateAsync())!;
        await h.States.UpsertAsync(state with { ExtraAllowanceBytes = 2000 }, Ct);
        await h.EvaluateAsync();
        Assert.Single(h.Provider.UnblockRequests);
        h.Usage.CurrentDayBytes = 3000;
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        Assert.Equal(2, h.Provider.BlockedMacs.Count);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task FailedApply_IsUnactioned_AndRetriesNextTickWithoutDuplicateNotifications(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action));
        h.Usage.CurrentDayBytes = 1000;
        h.Provider.ThrowOnNextWrite = true;
        await h.EvaluateAsync();

        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Contains(h.Log.Entries, e => e.Type == ActivityEventType.WriteFailed);
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        await h.EvaluateAsync();

        Assert.Single(await h.States.ListWithActiveActionAsync(Ct));
        Assert.Equal(1, h.Provider.AppliedLimits.Count + h.Provider.BlockedMacs.Count);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.ActionApplied);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task FailedRevert_PreservesActionAndToken_AndRetriesAfterRestart(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action));
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        var original = (await h.StateAsync())!;
        h.Provider.ThrowOnNextWrite = true;
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.EvaluateAsync();

        var active = Assert.Single(await h.States.ListWithActiveActionAsync(Ct));
        Assert.Equal(original.BlockToken, active.BlockToken);
        Assert.Equal(action == QuotaAction.Block, h.Device.IsBlocked);
        Assert.Equal(action == QuotaAction.Throttle, h.Device.QuotaThrottle is not null);
        Assert.DoesNotContain(h.Events.Events, e => e.Kind == QuotaEventKind.Reverted);
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Reverted);
        if (action == QuotaAction.Block)
            Assert.Equal(original.BlockToken, Assert.Single(h.Provider.UnblockRequests).Token);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task Restart_DoesNotRepeatActionsOrEvents(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action));
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        var eventsBeforeRestart = h.Events.Events.ToArray();
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        await h.ReconcileAsync();
        Assert.Equal(eventsBeforeRestart, h.Events.Events);
        Assert.Equal(1, h.Provider.AppliedLimits.Count + h.Provider.BlockedMacs.Count);
    }

    [Fact]
    public async Task FailingDevice_DoesNotPreventOtherDevicesBeingEvaluated()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Usage.CurrentDayBytes = 1000;
        var other = new Device { Mac = new("AA-BB-CC-DD-EE-02"), Hostname = "second", CategoryKey = "mobile" };
        await h.Devices.UpsertAsync(other, Ct);
        await h.Usages.UpsertStateAsync(new() { Mac = other.Mac, CurrentDayKey = h.Clock.TodayKey(), CurrentDayBytes = 1000 }, Ct);
        h.Provider.ThrowOnNextWrite = true;

        await h.EvaluateAsync();

        Assert.False(h.Device.IsBlocked);
        Assert.True(other.IsBlocked);
        Assert.Equal((string)other.Mac, Assert.Single(h.Provider.BlockedMacs));
        Assert.Equal(2, h.Events.Events.Count(e => e.Kind == QuotaEventKind.Exceeded));
        await h.EvaluateAsync();
        Assert.True(h.Device.IsBlocked);
        Assert.Equal(2, h.Provider.BlockedMacs.Count);
    }

    [Fact]
    public async Task FailingEventSink_DoesNotPreventActionsOrOtherSinks()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Usage.CurrentDayBytes = 1000;
        var service = h.NewService(sinks: [new FailingSink(), h.Events, new ActivityLogQuotaEventSink(h.Log)]);
        await service.EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        Assert.True(h.Device.IsBlocked);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        Assert.Single(h.Log.Entries, e => e.Type == ActivityEventType.QuotaActionApplied);
        Assert.Contains(h.Log.Entries, e => e.Type == ActivityEventType.WriteFailed && e.Message.Contains("sink"));
    }

    [Fact]
    public async Task CapPlusAllowanceBeyondLongMaxValue_DoesNotOverflowOrTriggerAction()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block) with { DailyBytes = long.MaxValue });
        await h.States.UpsertAsync(new() { Mac = h.Device.Mac, PeriodKind = QuotaPeriodKind.Day,
            PeriodKey = h.Clock.TodayKey(), ExtraAllowanceBytes = long.MaxValue }, Ct);
        h.Usage.CurrentDayBytes = long.MaxValue;
        await h.EvaluateAsync();
        Assert.Empty(h.Events.Events);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.DoesNotContain(h.Log.Entries, e => e.Type == ActivityEventType.WriteFailed);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task PersistedAction_RepairsMissingDeviceProjectionWithoutRepeatingTheAction(QuotaAction action)
    {
        var h = await Harness.CreateAsync(Live(action));
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        var original = (await h.StateAsync())!;
        h.Device.IsBlocked = false;
        h.Device.BlockListToken = null;
        h.Device.QuotaThrottle = null;
        var notifications = h.Events.Events.Count;

        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.Equal(action == QuotaAction.Block, h.Device.IsBlocked);
        Assert.Equal(original.BlockToken, h.Device.BlockListToken);
        Assert.Equal(action == QuotaAction.Throttle ? new SpeedLimit(true, 512, 512) : null, h.Device.QuotaThrottle);
        Assert.Equal(notifications, h.Events.Events.Count);
        Assert.Equal(1, h.Provider.BlockedMacs.Count + h.Provider.AppliedLimits.Count);
    }

    [Theory]
    [InlineData("dry-run")]
    [InlineData("protected")]
    public async Task EnablingEnforcementAfterARecordedSkip_AppliesActionWithoutRepeatingExceeded(string reason)
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block) with { DryRun = reason == "dry-run" });
        if (reason == "protected")
            await h.Protected.AddAsync(h.Device.Mac, Ct);
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        Assert.Empty(h.Provider.BlockedMacs);
        await h.Quotas.UpsertAsync(Live(QuotaAction.Block), Ct);
        await h.Protected.RemoveAsync(h.Device.Mac, Ct);

        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        await h.EvaluateAsync();

        Assert.Single(h.Provider.BlockedMacs);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Exceeded);
        Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.ActionApplied);
    }

    [Fact]
    public async Task FailedRevertForOneDevice_DoesNotStrandAnotherDevice()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Usage.CurrentDayBytes = 1000;
        var other = new Device { Mac = new("AA-BB-CC-DD-EE-02"), Hostname = "second", CategoryKey = "mobile" };
        await h.Devices.UpsertAsync(other, Ct);
        await h.Usages.UpsertStateAsync(new() { Mac = other.Mac, CurrentDayKey = h.Clock.TodayKey(), CurrentDayBytes = 1000 }, Ct);
        await h.EvaluateAsync();
        Assert.Equal(2, h.Provider.BlockedMacs.Count);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        h.Provider.ThrowOnNextWrite = true;

        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.True(h.Device.IsBlocked);
        Assert.False(other.IsBlocked);
        Assert.Equal(h.Device.Mac, Assert.Single(await h.States.ListWithActiveActionAsync(Ct)).Mac);
        Assert.Equal((string)other.Mac, Assert.Single(h.Provider.UnblockRequests).Mac);
        await h.EvaluateAsync();
        Assert.False(h.Device.IsBlocked);
        Assert.Equal(2, h.Provider.UnblockRequests.Count);
    }

    [Fact]
    public async Task InterruptedThrottle_RetainsOriginalLimitInsteadOfAdoptingTheThrottledSnapshotAsPrior()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Throttle), configured: false);
        h.Usage.CurrentDayBytes = 1000;
        var throttled = new SpeedLimit(true, 512, 512);
        h.Device.RouterReportedLimit = new(true, 512, 512, null);
        h.Device.LastAppliedFingerprint = null;
        h.Provider.Devices[0] = h.Provider.Devices[0] with { CurrentLimit = h.Device.RouterReportedLimit };
        await h.States.UpsertAsync(new() { Mac = h.Device.Mac, PeriodKind = QuotaPeriodKind.Day,
            PeriodKey = h.Clock.TodayKey(), PriorLimit = Harness.Normal, AppliedThrottleKbps = 512, ExceededNotified = true }, Ct);

        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);

        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Equal(throttled, h.Device.QuotaThrottle);
        Assert.Equal(Harness.Normal, Assert.Single(await h.States.ListWithActiveActionAsync(Ct)).PriorLimit);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.NewService().EvaluateAsync(h.Provider, h.Provider.Devices, Ct);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(Harness.Normal, Assert.Single(h.Provider.AppliedLimits).Limit);
    }

    [Fact]
    public async Task JoiningAnExistingMonthlyAction_ReportsTheActualActionAfterPolicyEdit()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Throttle) with { DailyBytes = null, MonthlyBytes = 1000 });
        h.Usage.CurrentMonthBytes = 1000;
        await h.EvaluateAsync();
        await h.Quotas.UpsertAsync(Live(QuotaAction.Block) with { MonthlyBytes = 1000 }, Ct);
        h.Usage.CurrentDayBytes = 1000;

        await h.EvaluateAsync();

        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Single(h.Provider.AppliedLimits);
        Assert.Equal(2, h.Events.Events.Count(e => e.Kind == QuotaEventKind.ActionApplied));
        Assert.All(h.Events.Events.Where(e => e.Kind == QuotaEventKind.ActionApplied), e => Assert.Equal(QuotaAction.Throttle, e.Action));
        Assert.All(await h.States.ListWithActiveActionAsync(Ct), s => Assert.Equal(QuotaActionKind.Throttle, s.ActionKind));
    }

    [Fact]
    public async Task RevertEvent_ReflectsCurrentUsageAndEffectiveCapAfterAllowance()
    {
        var h = await Harness.CreateAsync(Live(QuotaAction.Block));
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateAsync();
        h.Usage.CurrentDayBytes = 1500;
        await h.States.UpsertAsync((await h.StateAsync())! with { ExtraAllowanceBytes = 1000 }, Ct);

        await h.EvaluateAsync();

        var reverted = Assert.Single(h.Events.Events, e => e.Kind == QuotaEventKind.Reverted);
        Assert.Equal(1500, reverted.UsedBytes);
        Assert.Equal(2000, reverted.CapBytes);
        Assert.Equal(75, reverted.Percent);
    }

    private static QuotaPolicy Live(QuotaAction action) => new()
    {
        Id = "default", DailyBytes = 1000, Action = action, ThrottleKbps = action == QuotaAction.Throttle ? 512 : null, DryRun = false
    };

    private sealed class CapturingSink : IQuotaEventSink
    {
        public List<QuotaEvent> Events { get; } = [];
        public Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct)
        {
            Events.Add(quotaEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSink : IQuotaEventSink
    {
        public Task PublishAsync(QuotaEvent quotaEvent, CancellationToken ct) => throw new InvalidOperationException("Sink unavailable");
    }

    private sealed class Harness
    {
        public static readonly SpeedLimit Normal = new(true, 5000, 1000);
        public FakeTimeProvider Time { get; } = new(new(2026, 7, 25, 20, 59, 59, TimeSpan.Zero));
        public UsagePeriodClock Clock { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryUsageStore Usages { get; } = new();
        public InMemoryPolicyStore Policies { get; } = new();
        public InMemoryQuotaStore Quotas { get; } = new();
        public InMemoryQuotaStateStore States { get; } = new();
        public InMemoryProtectedDeviceStore Protected { get; } = new();
        public InMemoryActivityLogStore Log { get; } = new();
        public CapturingSink Events { get; } = new();
        public FakeRouterProvider Provider { get; } = new(new() { SupportsSpeedLimit = true, SupportsDeviceCategorization = true });
        public Device Device { get; private set; } = null!;
        public DeviceUsageState Usage { get; private set; } = null!;
        public QuotaEnforcementService Service { get; private set; } = null!;
        public PolicyReconciliationService Reconciliation { get; }

        private Harness()
        {
            Clock = new(timeProvider: Time);
            Reconciliation = new(Devices, Policies, Log, new FixedCategoryClassifier("mobile"));
        }

        public static async Task<Harness> CreateAsync(QuotaPolicy? quota = null, bool configured = true)
        {
            var h = new Harness();
            h.Device = new() { Mac = new("AA-BB-CC-DD-EE-01"), Hostname = "phone", FriendlyName = "My phone", CategoryKey = "mobile",
                RouterReportedLimit = new(true, 5000, 1000, null), LastAppliedFingerprint = PolicyFingerprint.Compute("mobile", null, 1) };
            h.Usage = new() { Mac = h.Device.Mac, CurrentDayKey = h.Clock.TodayKey(), CurrentMonthKey = h.Clock.ThisMonthKey() };
            await h.Devices.UpsertAsync(h.Device, Ct);
            await h.Usages.UpsertStateAsync(h.Usage, Ct);
            await h.Policies.UpsertAsync(new("mobile", configured ? Normal : SpeedLimit.Unlimited, 1, configured), Ct);
            await h.Quotas.UpsertAsync(quota ?? new() { Id = "default", DailyBytes = 1000, DryRun = false }, Ct);
            h.Provider.Devices.Add(new(h.Device.Mac, "192.168.1.50", "phone", "Mobile", new(ConnectionMedium.Wifi5Ghz, true), h.Device.RouterReportedLimit, null));
            h.Service = h.NewService();
            return h;
        }

        public QuotaEnforcementService NewService(IUsageStore? usage = null, IEnumerable<IQuotaEventSink>? sinks = null) =>
            new(Devices, usage ?? Usages, Quotas, States, Protected, Policies, Reconciliation, Log,
                sinks ?? [new NullQuotaEventSink(), Events, new ActivityLogQuotaEventSink(Log)], Clock);

        public Task EvaluateAsync() => Service.EvaluateAsync(Provider, Provider.Devices, Ct);
        public Task ReconcileAsync() => Reconciliation.ReconcileAsync(Provider, Ct);
        public Task<QuotaPeriodState?> StateAsync(QuotaPeriodKind kind = QuotaPeriodKind.Day) =>
            States.FindAsync(QuotaPeriodState.Key(Device.Mac, kind, kind == QuotaPeriodKind.Day ? Clock.TodayKey() : Clock.ThisMonthKey()), Ct);
    }
}
