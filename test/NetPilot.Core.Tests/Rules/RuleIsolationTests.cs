using NetPilot.Core.Devices;
using NetPilot.Core.Rules;
using static NetPilot.Core.Tests.Rules.RuleTestHarness;

namespace NetPilot.Core.Tests.Rules;

public class RuleIsolationTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("io")]
    [InlineData("timeout")]
    public async Task MissingOrThrowingDevice_IsNeverRestricted_AndOtherSubjectsStillApply(string failure)
    {
        var h = await CreateAsync();
        var other = await h.AddDeviceAsync(2);
        await h.SetUsageAsync(other, 1000, 1000);
        await h.AddRuleAsync(h.Rule(null, new Throttle(128)));
        var devices = new UnavailableDeviceStore(h.Devices, h.Device.Mac, failure);
        await h.NewEvaluator(devices).EvaluateAsync(h.Provider, [], Ct);
        Assert.Equal(other.Mac.ToString(), Assert.Single(h.Provider.AppliedLimits).Mac);
        Assert.Equal(other.Mac, Assert.Single(await h.Claims.GetAllAsync(Ct)).Mac);
        Assert.Equal(RuleFiringOutcome.Failed, Assert.Single(h.Firings.Items.Values, f => f.Subject == h.Device.Mac.ToString()).Outcome);
        Assert.Equal(RuleFiringOutcome.Applied, Assert.Single(h.Firings.Items.Values, f => f.Subject == other.Mac.ToString()).Outcome);
    }

    [Fact]
    public async Task NotificationTemplates_DoNotInterpretPlaceholdersIntroducedByReplacementValues()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule() with { Name = "Literal {device}" });
        await h.TickAsync();
        var message = Assert.Single(h.Channel.Messages);
        Assert.Equal("Literal {device}", message.Title);
        Assert.Contains("{unknown}", message.Body);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesWithoutRecordingRuleFailure()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(null, new Block()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.NewEvaluator().EvaluateAsync(h.Provider, [], cancellation.Token));
        Assert.Empty(h.Provider.BlockedMacs);
        Assert.Empty(h.Firings.Items);
    }

    private sealed class UnavailableDeviceStore(IDeviceStore inner, MacAddress unavailable, string failure) : IDeviceStore
    {
        public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct) => inner.GetAllAsync(ct);
        public Task<Device?> FindByMacAsync(MacAddress mac, CancellationToken ct)
        {
            if (mac != unavailable) return inner.FindByMacAsync(mac, ct);
            if (failure == "io") throw new IOException("Simulated per-device failure");
            if (failure == "timeout") throw new OperationCanceledException("Provider timeout without caller cancellation");
            return Task.FromResult<Device?>(null);
        }
        public Task UpsertAsync(Device device, CancellationToken ct) => inner.UpsertAsync(device, ct);
    }
}
