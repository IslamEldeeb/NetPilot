using NetPilot.Abstractions;
using NetPilot.Core.Policy;

namespace NetPilot.Core.Tests;

public class DevicePolicyTests
{
    [Fact]
    public void WithLimit_SameValue_DoesNotBumpVersion()
    {
        var policy = new DevicePolicy("mobile", new SpeedLimit(true, 3000, 1000), DefinitionVersion: 5, IsUserConfigured: true);

        var resaved = policy.WithLimit(new SpeedLimit(true, 3000, 1000));

        Assert.Equal(5, resaved.DefinitionVersion);
    }

    [Fact]
    public void WithLimit_DifferentValue_BumpsVersion()
    {
        var policy = new DevicePolicy("mobile", new SpeedLimit(true, 3000, 1000), DefinitionVersion: 5, IsUserConfigured: true);

        var updated = policy.WithLimit(new SpeedLimit(true, 5000, 1000));

        Assert.Equal(6, updated.DefinitionVersion);
        Assert.Equal(5000, updated.Limit.DownloadKbps);
    }

    [Theory]
    [InlineData(false, 1, false)]  // freshly auto-seeded fallback — never touched by a human
    [InlineData(true, 1, true)]    // limit is active — only possible via a real edit
    [InlineData(false, 2, true)]   // version bumped past 1 — only WithLimit does that, and only on an actual edit
    [InlineData(true, 2, true)]
    public void InferConfiguredFromLegacy_MatchesExpectedTruthTable(bool limitEnabled, int definitionVersion, bool expected)
    {
        Assert.Equal(expected, DevicePolicy.InferConfiguredFromLegacy(limitEnabled, definitionVersion));
    }
}
