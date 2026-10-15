using NetPilot.Core.Devices;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Tests.Quota;

public class QuotaPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrottleRequiresPositiveLimit(int? kbps) =>
        Assert.NotEmpty((new QuotaPolicy { Id = "default", Action = QuotaAction.Throttle, ThrottleKbps = kbps }).Validate());

    [Theory]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    [InlineData(null, 0L)]
    [InlineData(null, -1L)]
    public void SetCapsMustBePositive(long? daily, long? monthly) =>
        Assert.NotEmpty((new QuotaPolicy { Id = "default", DailyBytes = daily, MonthlyBytes = monthly }).Validate());

    [Theory]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { 100 })]
    [InlineData(new int[] { 80, 80 })]
    [InlineData(new int[] { 90, 80 })]
    public void WarningThresholdsMustBeInRangeDistinctAndSorted(int[] percents) =>
        Assert.NotEmpty((new QuotaPolicy { Id = "default", WarnPercents = percents }).Validate());

    [Theory]
    [InlineData(QuotaScope.Default, "cat:mobile", null, null)]
    [InlineData(QuotaScope.Default, "default", "mobile", null)]
    [InlineData(QuotaScope.Category, "cat:mobile", null, null)]
    [InlineData(QuotaScope.Category, "cat:laptop", "mobile", null)]
    [InlineData(QuotaScope.Device, "dev:AA-BB-CC-DD-EE-01", null, null)]
    [InlineData(QuotaScope.Device, "dev:AA-BB-CC-DD-EE-02", null, "AA-BB-CC-DD-EE-01")]
    [InlineData(QuotaScope.Device, "dev:AA-BB-CC-DD-EE-01", "mobile", "AA-BB-CC-DD-EE-01")]
    [InlineData((QuotaScope)99, "default", null, null)]
    public void ScopeAndIdentityMustAgree(QuotaScope scope, string id, string? category, string? mac) =>
        Assert.NotEmpty((new QuotaPolicy
        {
            Id = id, Scope = scope, CategoryKey = category, Mac = mac is null ? null : new MacAddress(mac)
        }).Validate());

    [Fact]
    public void ValidScopesAllowNullCapsAndEmptyWarnings()
    {
        QuotaPolicy[] policies =
        [
            new() { Id = "default", WarnPercents = [] },
            new() { Id = "cat:new_category", Scope = QuotaScope.Category, CategoryKey = "new_category", Exempt = true },
            new() { Id = "dev:AA-BB-CC-DD-EE-01", Scope = QuotaScope.Device, Mac = new("aa:bb:cc:dd:ee:01"),
                DailyBytes = 1, MonthlyBytes = long.MaxValue, Action = QuotaAction.Throttle, ThrottleKbps = 1, WarnPercents = [1, 80, 99] }
        ];
        Assert.All(policies, p => Assert.Empty(p.Validate()));
    }
}
