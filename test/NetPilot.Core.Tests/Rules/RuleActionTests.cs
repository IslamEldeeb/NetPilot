using NetPilot.Abstractions;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Tests.Fakes;
using static NetPilot.Core.Tests.Rules.RuleTestHarness;

namespace NetPilot.Core.Tests.Rules;

public class RuleActionTests
{
    [Fact]
    public async Task Cooldown_IsPerSubject_AndAllowsAnotherFiringAtBoundary()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync();
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(59);
        var other = await h.AddDeviceAsync(2);
        await h.SetUsageAsync(other, 1000, 5000);
        await h.TickAsync();
        Assert.Equal(2, h.Channel.Messages.Count);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(1);
        await h.TickAsync();
        Assert.Equal(3, h.Channel.Messages.Count);
        Assert.Equal(2, h.Firings.Items.Values.Count(f => f.Subject == h.Device.Mac.ToString()));
    }

    [Theory]
    [InlineData(RuleOncePer.Day)]
    [InlineData(RuleOncePer.Month)]
    public async Task OncePer_SuppressesUntilLocalPeriodChanges(RuleOncePer once)
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule() with { OncePer = once, CooldownMinutes = 0 });
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddHours(2);
        await h.TickAsync();
        Assert.Single(h.Channel.Messages);
        h.Time.UtcNow = h.Time.UtcNow.AddDays(1);
        await h.SetUsageAsync(h.Device, 1000, 5000);
        await h.TickAsync();
        Assert.Equal(once == RuleOncePer.Day ? 2 : 1, h.Channel.Messages.Count);
        if (once == RuleOncePer.Month)
        {
            h.Time.UtcNow = h.Time.UtcNow.AddMonths(1);
            await h.SetUsageAsync(h.Device, 1000, 5000);
            await h.TickAsync();
            Assert.Equal(2, h.Channel.Messages.Count);
        }
    }

    [Theory]
    [InlineData(RuleOncePer.Day, "2026-07-25T20:59:59Z")]
    [InlineData(RuleOncePer.Month, "2026-07-31T20:59:59Z")]
    public async Task OncePer_NewPeriodCanFireEvenInsideCooldown(RuleOncePer once, string utc)
    {
        var h = await CreateAsync();
        h.Time.UtcNow = DateTimeOffset.Parse(utc);
        await h.SetUsageAsync(h.Device, 1000, 5000);
        await h.AddRuleAsync(h.Rule() with { OncePer = once, CooldownMinutes = int.MaxValue });
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.SetUsageAsync(h.Device, 1000, 5000);
        await h.TickAsync();
        Assert.Equal(2, h.Channel.Messages.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DryRun_PersistsAndNotifiesOnceAcrossRestart_WithoutAnyRouterWrites(bool explicitNotify)
    {
        var h = await CreateAsync();
        h.AddWireless(true);
        RuleAction[] actions = [new Throttle(128), new Block(), new SetWireless("guest-5g", false)];
        if (explicitNotify) actions = [new Notify("{rule} {device}", "{used}/{cap} {unknown}"), .. actions];
        var rule = await h.AddRuleAsync(h.Rule(null, actions) with { DryRun = true });
        await h.TickAsync();
        await h.TickAsync();
        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Provider.AppliedWirelessUpdates);
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Equal(RuleFiringOutcome.DryRun, h.OnlyFiring.Outcome);
        var notification = Assert.Single(h.Channel.Messages);
        Assert.StartsWith("[Dry-run]", notification.Title);
        Assert.Contains("throttle", notification.Body);
        Assert.Contains("guest-5g", notification.Body);
        Assert.Equal($"rule:{rule.Id}:{h.Device.Mac}:{h.OnlyFiring.EpisodeKey}", notification.Tag);
        if (explicitNotify) Assert.Contains("1000/1000 {unknown}", notification.Body);
        Assert.Contains(h.Activity.Entries, e => e.Type == ActivityEventType.RuleDryRun && e.Message.Contains(rule.Name));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ProtectedDevice_OnlySkipsRestriction_StillNotifies(bool block, bool dryRun)
    {
        var h = await CreateAsync();
        await h.Protected.AddAsync(h.Device.Mac, Ct);
        await h.AddRuleAsync(h.Rule(null, new Notify("Protected notice", "Rule matched."), block ? new Block() : new Throttle(128)) with { DryRun = dryRun });
        await h.TickAsync();
        await h.TickAsync();
        Assert.Equal(RuleFiringOutcome.SkippedProtected, h.OnlyFiring.Outcome);
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Empty(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Contains("Protected", Assert.Single(h.Channel.Messages).Body);
        Assert.Contains(h.Activity.Entries, e => e.Type == ActivityEventType.RuleActionSkippedProtected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReleasingEitherRuleOrQuota_PreservesTheOtherThrottle(bool releaseRule)
    {
        var h = await CreateAsync();
        var quotas = new InMemoryQuotaStore();
        var states = new InMemoryQuotaStateStore();
        await quotas.UpsertAsync(new() { Id = "default", DailyBytes = 1000,
            Action = QuotaAction.Throttle, ThrottleKbps = 512, DryRun = false }, Ct);
        var quota = new QuotaEnforcementService(h.Devices, h.Usage, quotas, states, h.Protected, h.Policies,
            h.Claims, h.Enforcement, h.Activity, [], h.Clock);
        await quota.EvaluateAsync(h.Provider, [], Ct);
        await h.AddRuleAsync(h.Rule(null, new Throttle(128)));
        await h.TickAsync();
        Assert.Equal(new SpeedLimit(true, 128, 128), h.Device.QuotaThrottle);
        Assert.Equal(2, (await h.Claims.GetAllAsync(Ct)).Count);

        if (releaseRule)
        {
            await h.RequestReleaseAsync();
            await h.TickAsync();
        }
        else
        {
            var state = Assert.Single(await states.ListWithActiveActionAsync(Ct));
            await states.UpsertAsync(state with { ReleaseRequested = true }, Ct);
            await quota.EvaluateAsync(h.Provider, [], Ct);
        }
        Assert.Equal(new SpeedLimit(true, releaseRule ? 512 : 128, releaseRule ? 512 : 128), h.Device.QuotaThrottle);
        var surviving = Assert.Single(await h.Claims.GetAllAsync(Ct));
        Assert.StartsWith(releaseRule ? "quota:" : "rule:", surviving.Source);
        Assert.DoesNotContain(h.Provider.AppliedLimits, write => write.Limit == Normal);
    }

    [Fact]
    public async Task DuplicateThrottleAndRestart_DoNotWriteTwice_AndBlockUsesPersistedReversalToken()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new Throttle(128), new Throttle(128), new Block(), new Block()));
        await h.TickAsync();
        await h.TickAsync();
        Assert.Single(h.Provider.AppliedLimits);
        Assert.Single(h.Provider.BlockedMacs);
        Assert.Equal(2, (await h.Claims.GetAllAsync(Ct)).Count);
        var token = h.Device.BlockListToken;
        await h.RequestReleaseAsync();
        await h.TickAsync();
        await h.TickAsync();
        Assert.Equal(token, Assert.Single(h.Provider.UnblockRequests).Token);
        Assert.False(h.Device.IsBlocked);
        Assert.Null(h.Device.QuotaThrottle);
    }

    [Fact]
    public async Task ForceDryRun_OverridesLiveRule_AndKillSwitchPreventsFirings()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new Block()));
        h.Options.Enabled = false;
        await h.TickAsync();
        Assert.Empty(h.Firings.Items);
        h.Options.Enabled = true;
        h.Options.ForceDryRun = true;
        await h.TickAsync();
        Assert.Equal(RuleFiringOutcome.DryRun, h.OnlyFiring.Outcome);
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Single(h.Channel.Messages);
    }

    [Fact]
    public async Task FailedRule_DoesNotStopOtherRulesOrLaterQuotaEvaluation_AndRetriesAfterRestart()
    {
        var h = await CreateAsync();
        var bad = await h.AddRuleAsync(h.Rule(null, new Block()));
        var good = await h.AddRuleAsync(h.Rule() with { Name = "Good rule" });
        h.Provider.ThrowOnNextWrite = true;
        await h.TickAsync();
        Assert.Equal(RuleFiringOutcome.Failed, Assert.Single(h.Firings.Items.Values, f => f.RuleId == bad.Id).Outcome);
        Assert.Equal(good.Name, Assert.Single(h.Channel.Messages).Title);
        Assert.Contains(h.Activity.Entries, e => e.Type == ActivityEventType.RuleFailed);
        var quotas = new InMemoryQuotaStore();
        await quotas.UpsertAsync(new() { Id = "default", DailyBytes = 1, DryRun = true }, Ct);
        var states = new InMemoryQuotaStateStore();
        await new QuotaEnforcementService(h.Devices, h.Usage, quotas, states, h.Protected, h.Policies,
            h.Claims, h.Enforcement, h.Activity, [], h.Clock).EvaluateAsync(h.Provider, [], Ct);
        Assert.True((await states.FindAsync(QuotaPeriodState.Key(h.Device.Mac, QuotaPeriodKind.Day, h.Clock.TodayKey()), Ct))!.ExceededNotified);
        await h.TickAsync();
        Assert.Single(h.Provider.BlockedMacs);
        Assert.Equal(RuleFiringOutcome.Applied, Assert.Single(h.Firings.Items.Values, f => f.RuleId == bad.Id).Outcome);
        Assert.Single(h.Channel.Messages);
    }
}
