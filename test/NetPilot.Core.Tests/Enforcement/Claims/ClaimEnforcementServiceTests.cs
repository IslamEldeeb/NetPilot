using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Enforcement.Claims;

public class ClaimEnforcementServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void EffectiveRestriction_BlockWins_ThenLowestSymmetricCap_ThenNone()
    {
        var mac = new MacAddress("aa:bb:cc:dd:ee:01");
        var slow = new ActionClaim(mac, ClaimKind.Throttle, "rule:test", 128, DateTimeOffset.UtcNow);
        var fast = slow with { Source = "quota:day:2026-07-25", ThrottleKbps = 512 };
        var block = slow with { Kind = ClaimKind.Block, Source = "rule:block", ThrottleKbps = null };

        Assert.Equal(new(true, null), EffectiveRestriction.Compute([slow, block, fast]));
        Assert.Equal(new(false, new SpeedLimit(true, 128, 128)), EffectiveRestriction.Compute([fast, slow]));
        Assert.Equal(new(false, new SpeedLimit(true, 512, 512)), EffectiveRestriction.Compute([fast]));
        Assert.Equal(new(false, null), EffectiveRestriction.Compute([]));
    }

    [Theory]
    [InlineData(ClaimKind.Throttle, true)]
    [InlineData(ClaimKind.Throttle, false)]
    [InlineData(ClaimKind.Block, true)]
    [InlineData(ClaimKind.Block, false)]
    public async Task MidnightReleasesQuota_LeavesRuleRestriction_AndLastReleaseRestoresOnce(ClaimKind kind, bool ruleFirst)
    {
        var h = await Harness.CreateAsync();
        var rule = h.Claim(kind, "rule:test", 256);
        if (ruleFirst)
            await h.AddAsync(rule);
        await h.Quotas.UpsertAsync(new() { Id = "default", DailyBytes = 1000, DryRun = false,
            Action = kind == ClaimKind.Block ? QuotaAction.Block : QuotaAction.Throttle,
            ThrottleKbps = kind == ClaimKind.Throttle ? 512 : null }, Ct);
        h.Usage.CurrentDayBytes = 1000;
        await h.EvaluateQuotaAsync();
        if (!ruleFirst)
            await h.AddAsync(rule);
        var limitsBefore = h.Provider.AppliedLimits.Count;
        var blocksBefore = h.Provider.BlockedMacs.Count;
        var token = h.Device.BlockListToken;

        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1); // Cairo midnight.
        await h.EvaluateQuotaAsync();
        await h.EvaluateQuotaAsync();

        Assert.Equal("rule:test", Assert.Single(await h.Claims.GetForDeviceAsync(h.Device.Mac, Ct)).Source);
        Assert.Equal(limitsBefore, h.Provider.AppliedLimits.Count);
        Assert.Equal(blocksBefore, h.Provider.BlockedMacs.Count);
        Assert.Empty(h.Provider.UnblockRequests);
        Assert.Equal(kind == ClaimKind.Block, h.Device.IsBlocked);
        Assert.Equal(kind == ClaimKind.Throttle ? new SpeedLimit(true, 256, 256) : null, h.Device.QuotaThrottle);
        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));

        await h.Claims.RemoveAsync(rule.Id, Ct);
        Assert.True(await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        Assert.True(await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
        if (kind == ClaimKind.Block)
            Assert.Equal(token, Assert.Single(h.Provider.UnblockRequests).Token);
        else
        {
            Assert.Equal(limitsBefore + 1, h.Provider.AppliedLimits.Count);
            Assert.Equal(Harness.Normal, h.Provider.AppliedLimits.Last().Limit);
        }
    }

    [Fact]
    public async Task RemovingMoreRestrictiveThrottle_RecomputesRemainingCap_WithoutLosingOriginalLimit()
    {
        var h = await Harness.CreateAsync(configured: false);
        var quota = h.Claim(ClaimKind.Throttle, "quota:day:2026-07-25", 512);
        var rule = h.Claim(ClaimKind.Throttle, "rule:test", 128);
        await h.AddAsync(quota);
        h.Device.RouterReportedLimit = new(true, 512, 512, null);
        await h.AddAsync(rule);
        await h.Claims.RemoveAsync(rule.Id, Ct);
        await h.ReconcileAsync();
        Assert.Equal(new SpeedLimit(true, 512, 512), h.Provider.AppliedLimits.Last().Limit);
        Assert.Equal(Harness.Normal, (await h.Claims.FindDeviceStateAsync(h.Device.Mac, Ct))!.PriorLimit);
        await h.Claims.RemoveAsync(quota.Id, Ct);
        await h.ReconcileAsync();
        Assert.Equal(Harness.Normal, h.Provider.AppliedLimits.Last().Limit);
    }

    [Theory]
    [InlineData(ClaimKind.Block)]
    [InlineData(ClaimKind.Throttle)]
    public async Task ManualBlockIsNeverAdoptedOrChanged_EvenWhenClaimsComeAndGo(ClaimKind kind)
    {
        var h = await Harness.CreateAsync();
        h.Device.IsBlocked = true;
        h.Device.BlockListToken = "manual-token";
        var claim = h.Claim(kind, "rule:test");
        await h.AddAsync(claim);
        await h.Claims.RemoveAsync(claim.Id, Ct);
        await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct);

        Assert.True(h.Device.IsBlocked);
        Assert.Equal("manual-token", h.Device.BlockListToken);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Provider.UnblockRequests);
        Assert.Empty(h.Provider.AppliedLimits);
    }

    [Theory]
    [InlineData(ClaimKind.Block)]
    [InlineData(ClaimKind.Throttle)]
    public async Task ReclaimIsAnIdempotentUpsert_AndRestartRepairsLostProjectionWithoutRouterWrite(ClaimKind kind)
    {
        var h = await Harness.CreateAsync();
        var claim = h.Claim(kind, "rule:test");
        await h.AddAsync(claim);
        await h.AddAsync(claim with { Reason = "same owner again" });
        Assert.Single(await h.Claims.GetAllAsync(Ct));
        var token = h.Device.BlockListToken;
        h.Device.IsBlocked = false;
        h.Device.BlockListToken = null;
        h.Device.QuotaThrottle = null;

        Assert.True(await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        Assert.Equal(kind == ClaimKind.Block, h.Device.IsBlocked);
        Assert.Equal(token, h.Device.BlockListToken);
        Assert.Equal(kind == ClaimKind.Throttle ? new SpeedLimit(true, 512, 512) : null, h.Device.QuotaThrottle);
        Assert.Equal(1, h.Provider.AppliedLimits.Count + h.Provider.BlockedMacs.Count);
    }

    [Theory]
    [InlineData(ClaimKind.Block)]
    [InlineData(ClaimKind.Throttle)]
    public async Task FailedApplyAndRelease_KeepClaimOrReversalData_AndRetryAfterRestart(ClaimKind kind)
    {
        var h = await Harness.CreateAsync(configured: false);
        var claim = h.Claim(kind, "rule:test");
        await h.Claims.UpsertAsync(claim, Ct);
        h.Provider.ThrowOnNextWrite = true;
        if (kind == ClaimKind.Block)
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.ReconcileAsync());
        else
            Assert.False(await h.ReconcileAsync());
        Assert.Single(await h.Claims.GetAllAsync(Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.True(await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        var reversal = await h.Claims.FindDeviceStateAsync(h.Device.Mac, Ct);
        await h.Claims.RemoveAsync(claim.Id, Ct);
        h.Provider.ThrowOnNextWrite = true;
        if (kind == ClaimKind.Block)
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.ReconcileAsync());
        else
            Assert.False(await h.ReconcileAsync());
        Assert.Equal(reversal, await h.Claims.FindDeviceStateAsync(h.Device.Mac, Ct));
        Assert.True(await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        Assert.True(await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.BlockListToken);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(kind == ClaimKind.Block ? 1 : 0, h.Provider.BlockedMacs.Count);
        Assert.Equal(kind == ClaimKind.Block ? 1 : 0, h.Provider.UnblockRequests.Count);
        Assert.Equal(kind == ClaimKind.Throttle ? 2 : 0, h.Provider.AppliedLimits.Count);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task LegacyActiveQuotaState_RecreatesMissingClaims_AndReleasesSafely(QuotaAction action)
    {
        var h = await Harness.CreateAsync(configured: false);
        var blocked = action == QuotaAction.Block;
        var throttle = new SpeedLimit(true, 512, 512);
        var state = new QuotaPeriodState { Mac = h.Device.Mac, PeriodKind = QuotaPeriodKind.Day,
            PeriodKey = h.Clock.TodayKey(), ActionKind = blocked ? QuotaActionKind.Block : QuotaActionKind.Throttle,
            ActionAppliedAtUtc = h.Time.UtcNow, BlockToken = blocked ? "persisted-token" : null,
            PriorLimit = blocked ? null : Harness.Normal, AppliedThrottleKbps = blocked ? null : 512,
            UsedBytes = 1000, CapBytes = 1000 };
        await h.States.UpsertAsync(state, Ct);
        h.Usage.CurrentDayBytes = 1000;
        if (!blocked)
            h.Device.LastAppliedFingerprint = NetPilot.Core.Policy.PolicyFingerprint.Compute("mobile", null, 1, throttle);

        await h.EvaluateQuotaAsync();
        Assert.Single(await h.Claims.GetAllAsync(Ct));
        Assert.Equal(blocked, h.Device.IsBlocked);
        Assert.Equal(blocked ? null : throttle, h.Device.QuotaThrottle);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Provider.AppliedLimits);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.EvaluateQuotaAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        if (blocked)
            Assert.Equal("persisted-token", Assert.Single(h.Provider.UnblockRequests).Token);
        else
            Assert.Equal(Harness.Normal, Assert.Single(h.Provider.AppliedLimits).Limit);
    }

    [Theory]
    [InlineData(QuotaAction.Block)]
    [InlineData(QuotaAction.Throttle)]
    public async Task FailedQuotaApplyBeforeMidnight_DoesNotLeaveAnOrphanClaim(QuotaAction action)
    {
        var h = await Harness.CreateAsync();
        await h.Quotas.UpsertAsync(new() { Id = "default", DailyBytes = 1000, Action = action,
            ThrottleKbps = action == QuotaAction.Throttle ? 512 : null, DryRun = false }, Ct);
        h.Usage.CurrentDayBytes = 1000;
        h.Provider.ThrowOnNextWrite = true;
        await h.EvaluateQuotaAsync();
        Assert.Single(await h.Claims.GetAllAsync(Ct));
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.EvaluateQuotaAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.UnblockRequests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BlockDominatesThrottle_NoLimitWritesUntilUnblock_ThenRestoresOriginal(bool releaseBlockFirst)
    {
        var h = await Harness.CreateAsync();
        var block = h.Claim(ClaimKind.Block, "rule:block");
        var throttle = h.Claim(ClaimKind.Throttle, "rule:test");
        await h.AddAsync(block);
        await h.AddAsync(throttle);
        Assert.Empty(h.Provider.AppliedLimits);
        await h.Claims.RemoveAsync(releaseBlockFirst ? block.Id : throttle.Id, Ct);
        await h.ReconcileAsync();
        if (releaseBlockFirst)
        {
            Assert.Single(h.Provider.UnblockRequests);
            Assert.Equal(new SpeedLimit(true, 512, 512), Assert.Single(h.Provider.AppliedLimits).Limit);
        }
        else
        {
            Assert.Empty(h.Provider.UnblockRequests);
            Assert.Empty(h.Provider.AppliedLimits);
            Assert.True(h.Device.IsBlocked);
        }
        await h.Claims.RemoveAsync(releaseBlockFirst ? throttle.Id : block.Id, Ct);
        await h.ReconcileAsync();
        if (releaseBlockFirst)
            Assert.Equal(Harness.Normal, h.Provider.AppliedLimits.Last().Limit);
        Assert.Single(h.Provider.UnblockRequests);
        // A throttle removed while block dominates was never sent, so its original limit still matches.
        Assert.Equal(releaseBlockFirst ? 2 : 0, h.Provider.AppliedLimits.Count);
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Null((await h.Claims.FindDeviceStateAsync(h.Device.Mac, Ct))!.PriorLimit);
    }

    [Fact]
    public async Task BlockTokenAndCompletedUnblock_SurviveFailedDeviceProjectionSave()
    {
        var h = await Harness.CreateAsync();
        var failingDevices = new FailingDeviceStore(h.Devices);
        var claim = h.Claim(ClaimKind.Block, "rule:test");
        await h.Claims.UpsertAsync(claim, Ct);
        failingDevices.FailNextSave = true;
        await Assert.ThrowsAsync<IOException>(() => new ClaimEnforcementService(h.Claims, failingDevices, h.Policies, h.Policy)
            .ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        var token = (await h.Claims.FindDeviceStateAsync(h.Device.Mac, Ct))!.BlockToken;
        Assert.NotNull(token);
        h.Device.IsBlocked = false;
        h.Device.BlockListToken = null;
        await h.ReconcileAsync();
        Assert.Equal(token, h.Device.BlockListToken);
        Assert.Single(h.Provider.BlockedMacs);

        await h.Claims.RemoveAsync(claim.Id, Ct);
        failingDevices.FailNextSave = true;
        await Assert.ThrowsAsync<IOException>(() => new ClaimEnforcementService(h.Claims, failingDevices, h.Policies, h.Policy)
            .ReconcileDeviceAsync(h.Provider, h.Device, Ct));
        h.Device.IsBlocked = true;
        h.Device.BlockListToken = token;
        await h.NewService().ReconcileDeviceAsync(h.Provider, h.Device, Ct);
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.BlockListToken);
        Assert.Single(h.Provider.UnblockRequests);
    }

    [Theory]
    [InlineData("dry-run")]
    [InlineData("protected")]
    public async Task FailedQuotaApply_StillRespectsNewSafetySettingsOnRetry(string setting)
    {
        var h = await Harness.CreateAsync();
        var policy = new QuotaPolicy { Id = "default", DailyBytes = 1000, Action = QuotaAction.Block, DryRun = false };
        await h.Quotas.UpsertAsync(policy, Ct);
        h.Usage.CurrentDayBytes = 1000;
        h.Provider.ThrowOnNextWrite = true;
        await h.EvaluateQuotaAsync();
        if (setting == "dry-run")
            await h.Quotas.UpsertAsync(policy with { DryRun = true }, Ct);
        else
            await h.Protected.AddAsync(h.Device.Mac, Ct);
        await h.EvaluateQuotaAsync();
        Assert.False(h.Device.IsBlocked);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(await h.States.ListWithActiveActionAsync(Ct));
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
    }

    private sealed class FailingDeviceStore(IDeviceStore inner) : IDeviceStore
    {
        public bool FailNextSave { get; set; }
        public Task<Device?> FindByMacAsync(MacAddress mac, CancellationToken ct) => inner.FindByMacAsync(mac, ct);
        public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct) => inner.GetAllAsync(ct);
        public Task UpsertAsync(Device device, CancellationToken ct)
        {
            if (!FailNextSave) return inner.UpsertAsync(device, ct);
            FailNextSave = false;
            throw new IOException("Simulated projection persistence failure");
        }
    }

    private sealed class Harness
    {
        public static readonly SpeedLimit Normal = new(true, 5000, 1000);
        public Device Device { get; } = new() { Mac = new("aa:bb:cc:dd:ee:01"), CategoryKey = "mobile",
            RouterReportedLimit = new(true, 5000, 1000, null),
            LastAppliedFingerprint = NetPilot.Core.Policy.PolicyFingerprint.Compute("mobile", null, 1) };
        public FakeTimeProvider Time { get; } = new(new(2026, 7, 25, 20, 59, 59, TimeSpan.Zero));
        public UsagePeriodClock Clock { get; }
        public InMemoryDeviceStore Devices { get; } = new();
        public InMemoryPolicyStore Policies { get; } = new();
        public InMemoryActionClaimStore Claims { get; } = new();
        public InMemoryQuotaStateStore States { get; } = new();
        public InMemoryQuotaStore Quotas { get; } = new();
        public InMemoryProtectedDeviceStore Protected { get; } = new();
        public InMemoryUsageStore Usages { get; } = new();
        public DeviceUsageState Usage { get; }
        public InMemoryActivityLogStore Log { get; } = new();
        public FakeRouterProvider Provider { get; } = new(new() { SupportsSpeedLimit = true });
        public PolicyReconciliationService Policy { get; }
        private Harness()
        {
            Clock = new(timeProvider: Time);
            Usage = new() { Mac = Device.Mac, CurrentDayKey = Clock.TodayKey() };
            Policy = new(Devices, Policies, Log, new FixedCategoryClassifier("mobile"));
        }
        public static async Task<Harness> CreateAsync(bool configured = true)
        {
            var h = new Harness();
            await h.Devices.UpsertAsync(h.Device, Ct);
            await h.Policies.UpsertAsync(new("mobile", Normal, 1, configured), Ct);
            await h.Usages.UpsertStateAsync(h.Usage, Ct);
            return h;
        }
        public ClaimEnforcementService NewService() => new(Claims, Devices, Policies, Policy);
        public ActionClaim Claim(ClaimKind kind, string source, int kbps = 512) =>
            new(Device.Mac, kind, source, kind == ClaimKind.Throttle ? kbps : null, Time.UtcNow);
        public async Task AddAsync(ActionClaim claim)
        {
            await Claims.UpsertAsync(claim, Ct);
            Assert.True(await ReconcileAsync());
        }
        public Task<bool> ReconcileAsync() => NewService().ReconcileDeviceAsync(Provider, Device, Ct);
        public Task EvaluateQuotaAsync() => new QuotaEnforcementService(Devices, Usages, Quotas, States,
            Protected, Policies, Claims, NewService(), Log, [], Clock)
            .EvaluateAsync(Provider, [], Ct);
    }
}
