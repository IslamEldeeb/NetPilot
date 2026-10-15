using NetPilot.Core.Devices;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Tests.Quota;

public class QuotaResolverTests
{
    private static readonly Device Phone = new() { Mac = new("AA-BB-CC-DD-EE-01"), CategoryKey = "mobile" };
    private static readonly QuotaPolicy Default = new() { Id = "default", DailyBytes = 10_000 };
    private static readonly QuotaPolicy Category = new()
    {
        Id = "cat:mobile", Scope = QuotaScope.Category, CategoryKey = "mobile", DailyBytes = 5_000
    };
    private static readonly QuotaPolicy Device = new()
    {
        Id = $"dev:{Phone.Mac}", Scope = QuotaScope.Device, Mac = Phone.Mac, DailyBytes = 1_000
    };

    [Fact]
    public void DeviceBeatsCategoryAndDefault_RegardlessOfListOrder()
    {
        var resolved = QuotaResolver.Resolve(Phone, [Default, Category, Device]);
        Assert.Equal(QuotaResolutionKind.Policy, resolved.Kind);
        Assert.Equal(Device, resolved.MatchedPolicy);
    }

    [Fact]
    public void CategoryBeatsDefault_WhenOtherDeviceOverrideExists()
    {
        var other = Device with { Id = "dev:AA-BB-CC-DD-EE-02", Mac = new("AA-BB-CC-DD-EE-02") };
        Assert.Equal(Category, QuotaResolver.Resolve(Phone, [Default, other, Category]).MatchedPolicy);
    }

    [Fact]
    public void UnmatchedCategoryFallsBackToDefault()
    {
        var other = Category with { Id = "cat:laptop", CategoryKey = "laptop" };
        Assert.Equal(Default, QuotaResolver.Resolve(Phone, [other, Default]).MatchedPolicy);
    }

    [Theory]
    [InlineData(QuotaScope.Device)]
    [InlineData(QuotaScope.Category)]
    public void ExemptMatchStopsResolutionUnderCappedFallback(QuotaScope scope)
    {
        var exempt = (scope == QuotaScope.Device ? Device : Category) with { Exempt = true };
        var resolved = QuotaResolver.Resolve(Phone, new[] { Default, Category, exempt }.Where(p => p.Id != exempt.Id).Append(exempt).ToList());
        Assert.Equal(QuotaResolutionKind.Exempt, resolved.Kind);
        Assert.Equal(exempt, resolved.MatchedPolicy);
    }

    [Fact]
    public void NoPoliciesIsNotConfigured_InsteadOfExempt()
    {
        var resolved = QuotaResolver.Resolve(Phone, []);
        Assert.Equal(QuotaResolutionKind.NotConfigured, resolved.Kind);
        Assert.Null(resolved.MatchedPolicy);
    }

    [Fact]
    public void DeviceWithNullCapsWinsOverCappedCategory_WithoutBeingExempt()
    {
        var noCap = Device with { DailyBytes = null, MonthlyBytes = null };
        var resolved = QuotaResolver.Resolve(Phone, [Category, Default, noCap]);
        Assert.Equal(QuotaResolutionKind.Policy, resolved.Kind);
        Assert.Equal(noCap, resolved.MatchedPolicy);
    }
}
