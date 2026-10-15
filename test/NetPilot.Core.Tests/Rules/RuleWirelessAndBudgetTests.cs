using NetPilot.Core.Rules;
using static NetPilot.Core.Tests.Rules.RuleTestHarness;

namespace NetPilot.Core.Tests.Rules;

public class RuleWirelessAndBudgetTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Wireless_AppliesOnce_AndDeletedRuleRestoresPriorState(bool prior, bool desired)
    {
        var h = await CreateAsync();
        var id = h.AddWireless(prior);
        var rule = await h.AddRuleAsync(h.Rule(null, new SetWireless(id.ToString(), desired)));
        await h.TickAsync();
        await h.TickAsync();
        Assert.Equal(desired, h.Provider.WirelessNetworks[id].Enabled);
        Assert.Single(h.Provider.AppliedWirelessUpdates);
        var captured = Assert.Single(h.OnlyFiring.WirelessPrior);
        Assert.Equal(prior, captured.PriorEnabled);
        Assert.Equal(desired, captured.AppliedEnabled);
        await h.Rules.DeleteAsync(rule.Id, Ct);
        await h.TickAsync();
        await h.TickAsync();
        Assert.Equal(prior, h.Provider.WirelessNetworks[id].Enabled);
        Assert.Equal(2, h.Provider.AppliedWirelessUpdates.Count);
        Assert.Equal(RuleFiringStatus.Reverted, h.OnlyFiring.Status);
    }

    [Fact]
    public async Task Wireless_AlreadyDesired_IsRecordedWithoutWriteOrRestorationOwnership()
    {
        var h = await CreateAsync();
        var id = h.AddWireless(false);
        var rule = await h.AddRuleAsync(h.Rule(null, new SetWireless(id.ToString(), false)));
        await h.TickAsync();
        Assert.Equal(RuleFiringOutcome.Applied, h.OnlyFiring.Outcome);
        Assert.Contains("already", h.OnlyFiring.Error);
        Assert.Empty(h.OnlyFiring.WirelessPrior);
        await h.Rules.DeleteAsync(rule.Id, Ct);
        await h.TickAsync();
        Assert.Empty(h.Provider.AppliedWirelessUpdates);
    }

    [Fact]
    public async Task Wireless_RevertDoesNotOverwriteManualChange()
    {
        var h = await CreateAsync();
        var id = h.AddWireless(true);
        var rule = await h.AddRuleAsync(h.Rule(null, new SetWireless(id.ToString(), false)));
        await h.TickAsync();
        h.Provider.WirelessNetworks[id] = h.Provider.WirelessNetworks[id] with { Enabled = true };
        await h.Rules.UpsertAsync(rule with { Enabled = false }, Ct);
        await h.TickAsync();
        Assert.Single(h.Provider.AppliedWirelessUpdates);
        Assert.True(h.Provider.WirelessNetworks[id].Enabled);
        Assert.Equal(RuleFiringStatus.Reverted, h.OnlyFiring.Status);
        Assert.Contains(h.Activity.Entries, e => e.Message.Contains("differs from the rule's last write"));
    }

    [Fact]
    public async Task MainWireless_IsRejectedBeforeAnyFiringOrWrite()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new SetWireless("main-5g", false)));
        await h.TickAsync();
        Assert.Empty(h.Firings.Items);
        Assert.Empty(h.Provider.AppliedWirelessUpdates);
        Assert.Contains(h.Activity.Entries, e => e.Message.Contains("main networks are forbidden"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TickCap_DefersExcessFirings_AndRestartCompletesThemWithoutDuplicates(bool wireless)
    {
        var h = await CreateAsync();
        h.Options.MaxRouterActionsPerTick = 1;
        if (wireless)
        {
            h.AddWireless(true);
            await h.AddRuleAsync(h.Rule(null, new Throttle(128), new SetWireless("guest-5g", false)));
        }
        else
        {
            for (var n = 2; n <= 3; n++) await h.SetUsageAsync(await h.AddDeviceAsync(n), 1000, 1000);
            await h.AddRuleAsync(h.Rule(null, new Throttle(128)));
        }
        await h.TickAsync();
        Assert.Single(h.Provider.AppliedLimits);
        Assert.Empty(h.Provider.AppliedWirelessUpdates);
        Assert.Single(await h.Claims.GetAllAsync(Ct));
        for (var tick = 0; tick < (wireless ? 1 : 2); tick++) await h.TickAsync();
        Assert.All(h.Firings.Items.Values, f => Assert.True(f.Completed));
        Assert.Equal(wireless ? 1 : 3, h.Provider.AppliedLimits.Count);
        Assert.Equal(wireless ? 1 : 0, h.Provider.AppliedWirelessUpdates.Count);
        Assert.Equal(wireless ? 1 : 3, h.Firings.Items.Count);
        await h.TickAsync();
        Assert.Equal(wireless ? 1 : 3, h.Provider.AppliedLimits.Count);
    }

    [Fact]
    public async Task ZeroBudget_DoesNotPublishClaims_AndDeferredWorkCanBeReleasedWithoutWrite()
    {
        var h = await CreateAsync();
        h.Options.MaxRouterActionsPerTick = 0;
        await h.AddRuleAsync(h.Rule(null, new Block()));
        await h.TickAsync();
        Assert.False(h.OnlyFiring.Completed);
        Assert.Empty(await h.Claims.GetAllAsync(Ct));
        Assert.Empty(h.Provider.BlockedMacs);
        await h.RequestReleaseAsync();
        await h.TickAsync();
        Assert.Equal(RuleFiringStatus.Released, h.OnlyFiring.Status);
        Assert.Empty(h.Provider.BlockedMacs);
    }
}
