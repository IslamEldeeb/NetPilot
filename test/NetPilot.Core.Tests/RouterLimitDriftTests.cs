using NetPilot.Abstractions;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Tests;

public class RouterLimitDriftTests
{
    [Fact]
    public void BothDisabled_DifferentLeftoverKbps_Matches()
    {
        // Category toggled off in the UI but the Mbps boxes still hold old numbers — the
        // saved policy is (Enabled:false, 3000, 1000), the router reports true unlimited
        // (Enabled:false, null, null). Both render as "Unlimited"; must not be drift.
        var reported = new SpeedLimit(false, null, null);
        var desired = new SpeedLimit(false, 3000, 1000);

        Assert.True(RouterLimitDrift.Matches(reported, desired));
    }

    [Fact]
    public void BothDisabled_BothNull_Matches()
    {
        Assert.True(RouterLimitDrift.Matches(new SpeedLimit(false, null, null), new SpeedLimit(false, null, null)));
    }

    [Fact]
    public void BothEnabled_SameKbps_Matches()
    {
        Assert.True(RouterLimitDrift.Matches(new SpeedLimit(true, 5000, 1000), new SpeedLimit(true, 5000, 1000)));
    }

    [Fact]
    public void BothEnabled_DifferentKbps_IsDrift()
    {
        Assert.False(RouterLimitDrift.Matches(new SpeedLimit(true, 5000, 1000), new SpeedLimit(true, 3000, 1000)));
    }

    [Fact]
    public void OneEnabledOneDisabled_IsDrift()
    {
        Assert.False(RouterLimitDrift.Matches(new SpeedLimit(false, null, null), new SpeedLimit(true, 5000, 1000)));
    }
}
