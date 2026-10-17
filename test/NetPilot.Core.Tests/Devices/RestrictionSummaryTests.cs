using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;

namespace NetPilot.Core.Tests.Devices;

public class RestrictionSummaryTests
{
    [Fact]
    public void OverlappingClaims_ShowBlockThenLowestCapThenNoClaims()
    {
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        ActionClaim[] claims = [new(mac, ClaimKind.Throttle, "rule:a", 512, DateTimeOffset.UtcNow), new(mac, ClaimKind.Throttle, "quota:day:today", 256, DateTimeOffset.UtcNow)];
        Assert.Contains("256 Kbps", RestrictionSummary.Winner(claims));
        Assert.Contains("block", RestrictionSummary.Winner([.. claims, new(mac, ClaimKind.Block, "rule:b", null, DateTimeOffset.UtcNow)]));
        Assert.Contains("No active", RestrictionSummary.Winner([]));
    }
}
