using NetPilot.Abstractions;
using NetPilot.Core.Actions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Actions;

public class TemporaryActionTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("AA-BB-CC-DD-EE-01");

    [Theory]
    [InlineData(ClaimKind.Block)]
    [InlineData(ClaimKind.Throttle)]
    public async Task QueuedAction_AppliesOnTick_ExpiresAfterRestart_AndKeepsBasePolicy(ClaimKind kind)
    {
        var h = await Harness.CreateAsync();
        var request = new TemporaryRestrictionRequest(Guid.NewGuid(), [Mac], kind, 15, kind == ClaimKind.Throttle ? 512 : null, false);
        await h.Commands.RequestAsync(request, Ct);
        Assert.False(EffectiveRestriction.Compute(await h.Claims.GetAllAsync(Ct)).IsBlocked);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Provider.AppliedLimits);
        await h.TickAsync();
        var saved = Assert.Single(await h.Claims.GetAllAsync(Ct));
        Assert.False(saved.Pending);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(1);
        await h.Commands.RequestAsync(request, Ct);
        Assert.Equal(saved, Assert.Single(await h.Claims.GetAllAsync(Ct)));
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(14);
        await h.TickAsync();
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(new SpeedLimit(true, 5000, 1000), h.Device.Override);
        if (kind == ClaimKind.Block) Assert.Single(h.Provider.UnblockRequests);
        else Assert.Equal(h.Device.Override, h.Provider.AppliedLimits.Last().Limit);
    }

    [Fact]
    public async Task ExpiringPause_LeavesOverlappingThrottle_AndManualBlockOwnershipIsPreserved()
    {
        var h = await Harness.CreateAsync();
        await h.Claims.UpsertAsync(new(Mac, ClaimKind.Throttle, "rule:bedtime", 128, h.Time.UtcNow), Ct);
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false), Ct);
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(15);
        await h.TickAsync();
        Assert.Equal("rule:bedtime", Assert.Single(await h.Claims.GetAllAsync(Ct)).Source);
        Assert.Equal(new SpeedLimit(true, 128, 128), h.Device.QuotaThrottle);
        Assert.Single(h.Provider.UnblockRequests);
        h.Device.IsBlocked = true; h.Device.BlockListToken = "manual-token";
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false), Ct);
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(15);
        await h.TickAsync();
        Assert.Equal("manual-token", h.Device.BlockListToken);
        Assert.Single(h.Provider.UnblockRequests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DryRunOrForceDryRun_NeverWritesOrCapturesRestorationState(bool dryRun, bool forced)
    {
        var h = await Harness.CreateAsync();
        var commands = new TemporaryActions(h.Devices, h.Protection, h.Claims, h.Clock, new(forced));
        await commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Throttle, 15, 128, dryRun), Ct);
        await h.TickAsync();
        Assert.Null(await h.Claims.FindDeviceStateAsync(Mac, Ct));
        Assert.Empty(h.Provider.AppliedLimits);
        Assert.True(Assert.Single(await h.Claims.GetAllAsync(Ct)).DryRun);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(15);
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
    }

    [Fact]
    public async Task Protection_IsCheckedWhenQueuedAndAgainAtTick()
    {
        var h = await Harness.CreateAsync();
        await h.Protection.AddAsync(Mac, Ct);
        Assert.Equal(new(0, 1), await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false), Ct));
        await h.Protection.RemoveAsync(Mac, Ct);
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false), Ct);
        await h.Protection.AddAsync(Mac, Ct);
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Empty(h.Provider.BlockedMacs);
    }

    [Fact]
    public async Task FailedExpiryRestoration_RetainsPendingClaimForRetry()
    {
        var h = await Harness.CreateAsync();
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false), Ct);
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(15);
        h.Provider.ThrowOnNextWrite = true;
        await Assert.ThrowsAsync<AggregateException>(h.TickAsync);
        Assert.True(Assert.Single(await h.Claims.GetAllAsync(Ct)).Pending);
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Single(h.Provider.UnblockRequests);
    }

    [Fact]
    public async Task Bonus_IsNamedPeriod_Idempotent_Additive_AndRejectsStalePeriod()
    {
        var h = await Harness.CreateAsync();
        var policies = new InMemoryQuotaStore(); var states = new InMemoryQuotaStateStore();
        await policies.UpsertAsync(new() { Id = "default", DailyBytes = 5_000_000_000, MonthlyBytes = 30_000_000_000 }, Ct);
        var command = new QuotaBonusCommands(h.Devices, policies, states, h.Clock);
        var request = new QuotaBonusRequest(Guid.NewGuid(), Mac, QuotaPeriodKind.Day, h.Clock.TodayKey(), 1_000_000_000);
        await command.GrantAsync(request, Ct); await command.GrantAsync(request, Ct);
        var day = (await states.FindAsync(QuotaPeriodState.Key(Mac, QuotaPeriodKind.Day, h.Clock.TodayKey()), Ct))!;
        Assert.Equal(1_000_000_000, day.ExtraAllowanceBytes);
        Assert.Equal(6_000_000_000, day.EffectiveCapBytes);
        Assert.Null(await states.FindAsync(QuotaPeriodState.Key(Mac, QuotaPeriodKind.Month, h.Clock.ThisMonthKey()), Ct));
        await command.GrantAsync(request with { RequestId = Guid.NewGuid(), Period = QuotaPeriodKind.Month, PeriodKey = h.Clock.ThisMonthKey() }, Ct);
        Assert.Equal(1_000_000_000, (await states.FindAsync(QuotaPeriodState.Key(Mac, QuotaPeriodKind.Month, h.Clock.ThisMonthKey()), Ct))!.ExtraAllowanceBytes);
        Assert.Equal(5_000_000_000, (await policies.FindAsync("default", Ct))!.DailyBytes);
        h.Time.UtcNow = h.Time.UtcNow.AddDays(1);
        await Assert.ThrowsAsync<ArgumentException>(() => command.GrantAsync(request, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredBeforeFirstTick_NeverAppliesOrCapturesBaseState(bool dryRun)
    {
        var h = await Harness.CreateAsync();
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Throttle, 15, 128, dryRun), Ct);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(30);
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Null(await h.Claims.FindDeviceStateAsync(Mac, Ct));
        Assert.Empty(h.Provider.AppliedLimits);
    }

    [Fact]
    public async Task ExpiryRestoresLatestBaseEdit_InsteadOfOverwritingItWithCapturedLimit()
    {
        var h = await Harness.CreateAsync();
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Throttle, 15, 128, false), Ct);
        await h.TickAsync();
        h.Device.Override = new(true, 8000, 2000);
        await h.Devices.UpsertAsync(h.Device, Ct);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(15);
        await h.TickAsync();
        Assert.Equal(new SpeedLimit(true, 8000, 2000), h.Provider.AppliedLimits.Last().Limit);
        Assert.Equal(new SpeedLimit(true, 8000, 2000), h.Device.Override);
    }

    [Fact]
    public async Task FailingTarget_DoesNotPreventOtherTargetsFromApplying()
    {
        var h = await Harness.CreateAsync();
        var other = new MacAddress("AA-BB-CC-DD-EE-02");
        await h.Devices.UpsertAsync(new() { Mac = other }, Ct);
        await h.Commands.RequestAsync(new(Guid.NewGuid(), [Mac, other], ClaimKind.Block, 15, DryRun: false), Ct);
        h.Provider.ThrowOnNextWrite = true;
        await Assert.ThrowsAsync<AggregateException>(h.TickAsync);
        Assert.Equal(other.ToString(), Assert.Single(h.Provider.BlockedMacs));
        await h.TickAsync();
        Assert.Equal(2, h.Provider.BlockedMacs.Count);
    }

    private sealed class Harness
    {
        public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-10-16T12:00:00Z"));
        public UsagePeriodClock Clock { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryProtectedDeviceStore Protection { get; } = new();
        public InMemoryActionClaimStore Claims { get; } = new();
        public InMemoryPolicyStore Policies { get; } = new();
        public FakeRouterProvider Provider { get; } = new(new() { SupportsSpeedLimit = true });
        public Device Device { get; } = new() { Mac = Mac, Hostname = "TV", CategoryKey = "television", Override = new(true, 5000, 1000) };
        public TemporaryActions Commands { get; }
        private Harness() { Clock = new(timeProvider: Time); Commands = new(Devices, Protection, Claims, Clock, new(false)); }
        public static async Task<Harness> CreateAsync()
        {
            var h = new Harness();
            await h.Policies.UpsertAsync(new("television", SpeedLimit.Unlimited, 1, false), Ct);
            await h.Devices.UpsertAsync(h.Device, Ct);
            return h;
        }
        public Task TickAsync()
        {
            var reconciliation = new PolicyReconciliationService(Devices, Policies, new InMemoryActivityLogStore(), new HeuristicDeviceClassifier());
            var enforcement = new ClaimEnforcementService(Claims, Devices, Policies, reconciliation);
            return new TemporaryActionEnforcement(Claims, Devices, Protection, enforcement, Clock, new(false)).EvaluateAsync(Provider, Ct);
        }
    }
}
