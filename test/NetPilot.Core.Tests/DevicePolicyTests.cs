using NetPilot.Abstractions;
using NetPilot.Core.Policy;

namespace NetPilot.Core.Tests;

public class DevicePolicyTests
{
    [Fact]
    public void WithLimit_SameValue_DoesNotBumpVersion()
    {
        var policy = new DevicePolicy("mobile", new SpeedLimit(true, 3000, 1000), DefinitionVersion: 5);

        var resaved = policy.WithLimit(new SpeedLimit(true, 3000, 1000));

        Assert.Equal(5, resaved.DefinitionVersion);
    }

    [Fact]
    public void WithLimit_DifferentValue_BumpsVersion()
    {
        var policy = new DevicePolicy("mobile", new SpeedLimit(true, 3000, 1000), DefinitionVersion: 5);

        var updated = policy.WithLimit(new SpeedLimit(true, 5000, 1000));

        Assert.Equal(6, updated.DefinitionVersion);
        Assert.Equal(5000, updated.Limit.DownloadKbps);
    }
}
