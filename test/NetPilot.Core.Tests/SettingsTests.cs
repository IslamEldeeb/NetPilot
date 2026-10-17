using NetPilot.Abstractions;
using NetPilot.Core.Health;
using NetPilot.Core.Rules;
using NetPilot.Core.Safety;
using NetPilot.Core.Settings;

namespace NetPilot.Core.Tests;

public sealed class SettingsTests
{
    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("router.lan:443", true)]
    [InlineData("https://router", false)]
    [InlineData("user:password@router", false)]
    [InlineData("router?token=secret", false)]
    [InlineData("router/path", false)]
    [InlineData("router#secret", false)]
    [InlineData("", false)]
    public void RouterHostAcceptsOnlyBareHostOrAddress(string host, bool valid) => Assert.Equal(valid, RouterForms.ValidHost(host));

    [Theory]
    [InlineData(WirelessNetworkKind.Guest, "new-password")]
    [InlineData(WirelessNetworkKind.Iot, null)]
    public void WirelessDeltaPreservesUnchangedFields_AndNeverSendsIotPassword(WirelessNetworkKind kind, string? expectedPassword)
    {
        var network = Network(kind);
        var delta = RouterForms.WirelessDelta(network, network.Enabled, network.Ssid!, "new-password");
        Assert.Null(delta.Enabled); Assert.Null(delta.Ssid); Assert.Equal(expectedPassword, delta.Password);
        delta = RouterForms.WirelessDelta(network, false, "Changed", "");
        Assert.False(delta.Enabled); Assert.Equal("Changed", delta.Ssid); Assert.Null(delta.Password);
        Assert.Null(delta.Channel); Assert.Null(delta.TxPower);
    }

    [Fact]
    public void MainNetworkCannotBeEditedDespiteAdvertisedFeatures()
    {
        Assert.False(RouterForms.CanEdit(Network(WirelessNetworkKind.Main)));
        Assert.Throws<SettingsValidationException>(() => RouterForms.WirelessDelta(Network(WirelessNetworkKind.Main), false, "Changed", "secret"));
    }

    [Fact]
    public void UnsupportedWirelessFieldsAreNotSent()
    {
        var network = Network(WirelessNetworkKind.Guest) with { Features = new(true, false, false, false, false, false, false) };
        var delta = RouterForms.WirelessDelta(network, false, "Changed", "secret");
        Assert.False(delta.Enabled); Assert.Null(delta.Ssid); Assert.Null(delta.Password);
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    public void GraduationRequiresBothHostsToPermitRules_WithoutChangingOptions(bool webForce, bool agentForce, bool webEnabled, bool agentEnabled)
    {
        var now = DateTimeOffset.Parse("2026-10-17T10:00:00Z");
        var heartbeat = Healthy(now) with { EffectiveModes = new(agentForce, agentEnabled) };
        var options = new RulesOptions { ForceDryRun = webForce, Enabled = webEnabled };
        Assert.Equal(!webForce && !agentForce && webEnabled && agentEnabled,
            SafetyGate.BlockReason(SafetyPolicyKind.Rule, heartbeat, now, options) is null);
        Assert.Null(SafetyGate.BlockReason(SafetyPolicyKind.Quota, heartbeat, now, options));
        Assert.Equal(webForce, options.ForceDryRun); Assert.Equal(webEnabled, options.Enabled);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("failed")]
    [InlineData("no-router-read")]
    [InlineData("future")]
    public void GraduationRejectsUnknownStaleFailedAndFutureHealth(string scenario)
    {
        var now = DateTimeOffset.Parse("2026-10-17T10:00:00Z");
        AgentHeartbeat? heartbeat = scenario switch
        {
            "missing" => null, "stale" => Healthy(now.AddHours(-1)),
            "failed" => Healthy(now) with { ConsecutiveFailedTicks = 1 },
            "no-router-read" => Healthy(now) with { LastSuccessfulRouterReadUtc = null },
            _ => Healthy(now.AddMinutes(1))
        };
        Assert.NotNull(SafetyGate.BlockReason(SafetyPolicyKind.Quota, heartbeat, now, new()));
    }

    [Theory]
    [InlineData(19, 62, 1000)] [InlineData(500, 61, 1000)] [InlineData(500, 62, 99)]
    [InlineData(10001, 62, 1000)] [InlineData(500, 3651, 1000)]
    public void RetentionRejectsUnsafeOrUnboundedLimits(int attempts, int days, int firings) =>
        Assert.Throws<ArgumentException>(() => new RetentionPreferences { NotificationAttempts = attempts, InactiveQuotaDays = days, RuleFirings = firings }.Validate());

    private static WirelessNetworkState Network(WirelessNetworkKind kind) => new(new(kind, WirelessBand.Ghz24), "WiFi", true, "Home", true, "WPA2", null, null, null, new(true, true, true, true, true, true, true), []);
    private static AgentHeartbeat Healthy(DateTimeOffset now) => new() { LastTickStartedUtc = now.AddSeconds(-10), LastTickCompletedUtc = now.AddSeconds(-5), LastSuccessfulRouterReadUtc = now.AddSeconds(-6), TickIntervalSeconds = 180, EffectiveModes = new(false, true) };
}
