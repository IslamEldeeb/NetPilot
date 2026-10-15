using NetPilot.Abstractions;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Rules;
using static NetPilot.Core.Tests.Rules.RuleTestHarness;

namespace NetPilot.Core.Tests.Rules;

public class RuleRevertTests
{
    [Theory]
    [InlineData("release")]
    [InlineData("disabled")]
    [InlineData("deleted")]
    [InlineData("scope")]
    [InlineData("kill")]
    [InlineData("force-dry-run")]
    [InlineData("protected")]
    public async Task SafetyOrReleaseChange_ReleasesPersistedClaims(string change)
    {
        var h = await CreateAsync();
        var rule = await h.AddRuleAsync(h.Rule(null, new Throttle(128)));
        await h.TickAsync();
        switch (change)
        {
            case "release": await h.RequestReleaseAsync(); break;
            case "disabled": await h.Rules.UpsertAsync(rule with { Enabled = false }, Ct); break;
            case "deleted": await h.Rules.DeleteAsync(rule.Id, Ct); break;
            case "scope": await h.Rules.UpsertAsync(rule with { Scope = new() { Kind = RuleScopeKind.Category, CategoryKey = "tv" } }, Ct); break;
            case "kill": h.Options.Enabled = false; break;
            case "force-dry-run": h.Options.ForceDryRun = true; break;
            case "protected": await h.Protected.AddAsync(h.Device.Mac, Ct); break;
        }
        await h.TickAsync();
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Null(h.Device.QuotaThrottle);
        Assert.Equal(Normal, h.Provider.AppliedLimits.Last().Limit);
        Assert.NotEqual(RuleFiringStatus.Active, h.OnlyFiring.Status);
        Assert.Contains(h.Activity.Entries, e => e.Type == ActivityEventType.RuleReverted);
    }

    [Fact]
    public async Task AfterMinutes_RevertsAtExactDeadline()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new Throttle(128)) with { Revert = new(RuleRevertKind.AfterMinutes, 5) });
        await h.TickAsync();
        Assert.Equal(h.Time.UtcNow.AddMinutes(5), h.OnlyFiring.RevertAtUtc);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(5).AddTicks(-1);
        await h.TickAsync();
        Assert.Single(h.Provider.AppliedLimits);
        h.Time.UtcNow = h.Time.UtcNow.AddTicks(1);
        await h.TickAsync();
        Assert.Equal(Normal, h.Provider.AppliedLimits.Last().Limit);
        Assert.Equal(RuleFiringStatus.Reverted, h.OnlyFiring.Status);
    }

    [Theory]
    [InlineData("2026-07-25T20:59:59Z", false, false)]
    [InlineData("2026-01-25T21:59:59Z", false, false)]
    [InlineData("2026-04-23T21:59:59Z", false, false)]
    [InlineData("2026-07-31T20:59:59Z", true, false)]
    [InlineData("2026-07-25T20:59:59Z", false, true)]
    public async Task AtPeriodEnd_UsesCairoBoundaryIncludingDst(string utc, bool month, bool nonPeriod)
    {
        var h = await CreateAsync();
        h.Time.UtcNow = DateTimeOffset.Parse(utc);
        await h.SetUsageAsync(h.Device, 1000, 5000);
        RuleTrigger trigger = nonPeriod ? new DeviceJoined() : new UsageOver(month ? RulePeriod.Month : RulePeriod.Day, 1000);
        await h.AddRuleAsync(h.Rule(trigger, new Throttle(128)) with { Revert = new(RuleRevertKind.AtPeriodEnd) });
        await h.TickAsync();
        Assert.Single(await h.Claims.GetAllAsync(Ct));
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.TickAsync();
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Equal(RuleFiringStatus.Reverted, h.OnlyFiring.Status);
    }

    [Fact]
    public async Task WhenTriggerEnds_ReleasesAtWindowEnd_AndNextOccurrenceCanFire()
    {
        var h = await CreateAsync();
        h.Time.UtcNow = DateTimeOffset.Parse("2026-07-25T20:00:00Z");
        await h.AddRuleAsync(h.Rule(new TimeWindow("22:00", "07:00", Enum.GetValues<DayOfWeek>()), new Throttle(128)) with
        { Revert = new(RuleRevertKind.WhenTriggerEnds), CooldownMinutes = 0 });
        await h.TickAsync();
        h.Time.UtcNow = DateTimeOffset.Parse("2026-07-26T03:59:59Z");
        await h.TickAsync();
        Assert.Single(h.Provider.AppliedLimits);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.TickAsync();
        Assert.Equal(Normal, h.Provider.AppliedLimits.Last().Limit);
        h.Time.UtcNow = DateTimeOffset.Parse("2026-07-26T20:00:00Z");
        await h.TickAsync();
        Assert.Equal(2, h.Firings.Items.Count);
        Assert.Single(await h.Claims.GetAllAsync(Ct));
    }

    [Fact]
    public async Task FailedRevert_StaysActiveAndRetriesAfterRestart()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new Block()));
        await h.TickAsync();
        await h.RequestReleaseAsync();
        h.Provider.ThrowOnNextWrite = true;
        await h.TickAsync();
        Assert.Equal(RuleFiringStatus.Active, h.OnlyFiring.Status);
        Assert.Equal(RuleFiringOutcome.Failed, h.OnlyFiring.Outcome);
        Assert.True(h.Device.IsBlocked);
        await h.TickAsync();
        Assert.Equal(RuleFiringStatus.Released, h.OnlyFiring.Status);
        Assert.Single(h.Provider.UnblockRequests);
        Assert.False(h.Device.IsBlocked);
    }

    [Fact]
    public async Task RevertConsumesBudgetBeforeNewEffects_AndMultiWriteReconcileResumes()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new Throttle(128), new Block()));
        await h.TickAsync();
        await h.RequestReleaseAsync();
        h.Options.MaxRouterActionsPerTick = 1;
        await h.TickAsync();
        Assert.Single(h.Provider.UnblockRequests);
        Assert.Single(h.Provider.AppliedLimits);
        Assert.Equal(RuleFiringStatus.Active, h.OnlyFiring.Status);
        await h.TickAsync();
        Assert.Single(h.Provider.UnblockRequests);
        Assert.Equal(Normal, h.Provider.AppliedLimits.Last().Limit);
        Assert.Equal(RuleFiringStatus.Released, h.OnlyFiring.Status);
    }
}
