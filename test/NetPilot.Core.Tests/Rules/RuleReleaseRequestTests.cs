using NetPilot.Core.Rules;

namespace NetPilot.Core.Tests.Rules;

public class RuleReleaseRequestTests
{
    [Theory]
    [InlineData(RuleFiringStatus.Active, true)]
    [InlineData(RuleFiringStatus.Reverted, false)]
    [InlineData(RuleFiringStatus.Released, false)]
    public async Task ReleaseRequest_IsIdempotentAndOnlyChangesActiveRows(RuleFiringStatus status, bool expected)
    {
        var store = new MemoryFiringStore();
        var row = new RuleFiring { RuleId = Guid.NewGuid(), Subject = "household", EpisodeKey = "one", Status = status,
            Error = "retain", CompletedActionKeys = ["0"], AppliedClaimIds = ["claim"], Actions = [new Throttle(256)] };
        await store.UpdateAsync(row.Id, _ => row, CancellationToken.None);
        Assert.False(await store.RequestReleaseAsync("missing", CancellationToken.None));
        Assert.Equal(expected, await store.RequestReleaseAsync(row.Id, CancellationToken.None));
        Assert.Equal(expected, await store.RequestReleaseAsync(row.Id, CancellationToken.None));
        Assert.Equal(row with { ReleaseRequested = expected }, await store.FindAsync(row.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentAgentProgressAndWebRelease_PreserveBothUpdates()
    {
        var store = new MemoryFiringStore();
        var firing = new RuleFiring { RuleId = Guid.NewGuid(), Subject = "household", EpisodeKey = "one" };
        await store.UpdateAsync(firing.Id, _ => firing, CancellationToken.None);
        await Task.WhenAll(
            Task.Run(() => store.UpdateAsync(firing.Id, row => row! with { Completed = true, Error = "Agent progress", CompletedActionKeys = ["0", "1"] }, CancellationToken.None)),
            Task.Run(() => store.RequestReleaseAsync(firing.Id, CancellationToken.None)));
        var updated = (await store.FindAsync(firing.Id, CancellationToken.None))!;
        Assert.True(updated.ReleaseRequested);
        Assert.True(updated.Completed);
        Assert.Equal("Agent progress", updated.Error);
        Assert.Equal(new[] { "0", "1" }, updated.CompletedActionKeys);
    }

    [Fact]
    public async Task ReleaseThroughStore_IsConsumedByAgentAndRemovesOnlyRequestedRule()
    {
        var h = await RuleTestHarness.CreateAsync();
        await h.AddRuleAsync(h.Rule(actions: [new Throttle(128)]));
        await h.AddRuleAsync(h.Rule(actions: [new Throttle(256)]));
        await h.TickAsync();
        var firing = h.Firings.Items.Values.First();
        Assert.True(await h.Firings.RequestReleaseAsync(firing.Id, CancellationToken.None));
        await h.TickAsync();
        Assert.Equal(RuleFiringStatus.Released, h.Firings.Items[firing.Id].Status);
        Assert.Single(await h.Claims.GetAllAsync(CancellationToken.None));
        Assert.Single(h.Firings.Items.Values, f => f.Status == RuleFiringStatus.Active);
    }
}
