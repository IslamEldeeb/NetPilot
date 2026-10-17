using NetPilot.Core.Devices;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Tests.Devices;

public class QuotaPolicyTextTests
{
    private static Device Tv() => new()
    {
        Mac = new("AA-BB-CC-DD-EE-01"),
        FriendlyName = "Samsung TV",
        Hostname = "samsung-tv",
        CategoryKey = "television",
        FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-30)
    };

    private static QuotaPolicy CategoryPolicy(bool dryRun = true) => new()
    {
        Id = "cat:television", Scope = QuotaScope.Category, CategoryKey = "television",
        DailyBytes = 5_000_000_000, MonthlyBytes = null,
        Action = QuotaAction.Throttle, ThrottleKbps = 512, DryRun = dryRun
    };

    [Fact]
    public void EffectivePolicy_CategoryTrail_MatchesSpecWording()
    {
        var text = QuotaPolicyText.EffectivePolicy(Tv(),
            new QuotaResolution(QuotaResolutionKind.Policy, CategoryPolicy()));
        Assert.Equal("Inherits Category Television -> 5 GB/day -> Throttle 512 Kbps, dry-run", text);
    }

    [Fact]
    public void EffectivePolicy_IncludesMonthlyCapAndOmitsDryRunSuffixWhenLive()
    {
        var policy = CategoryPolicy(dryRun: false) with { MonthlyBytes = 300_000_000_000 };
        var text = QuotaPolicyText.EffectivePolicy(Tv(), new(QuotaResolutionKind.Policy, policy));
        Assert.Contains("5 GB/day", text);
        Assert.Contains("300 GB/month", text);
        Assert.DoesNotContain("dry-run", text);
    }

    [Fact]
    public void EffectivePolicy_DeviceOverrideAndNotConfiguredAndExempt()
    {
        var deviceOverride = new QuotaPolicy
        {
            Id = "dev:AA-BB-CC-DD-EE-01", Scope = QuotaScope.Device, Mac = new("AA-BB-CC-DD-EE-01"),
            DailyBytes = null, MonthlyBytes = null, Action = QuotaAction.NotifyOnly, DryRun = true
        };
        Assert.StartsWith("Device override for Samsung TV", QuotaPolicyText.EffectivePolicy(Tv(),
            new(QuotaResolutionKind.Policy, deviceOverride)));
        Assert.Equal("No quota policy is set for this device yet.",
            QuotaPolicyText.EffectivePolicy(Tv(), new(QuotaResolutionKind.NotConfigured, null)));
        Assert.Contains("exempt", QuotaPolicyText.EffectivePolicy(Tv(),
            new(QuotaResolutionKind.Exempt, CategoryPolicy())), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Meter_ShowsExactFiguresAndPercent()
    {
        var policy = CategoryPolicy();
        Assert.Equal("2.50 GB of 5.00 GB (50%)",
            QuotaPolicyText.Meter(new(QuotaResolutionKind.Policy, policy), 2_500_000_000));
        Assert.Equal("No daily cap", QuotaPolicyText.Meter(new(QuotaResolutionKind.NotConfigured, null), 0));
        Assert.Equal("No monthly cap", QuotaPolicyText.MonthlyMeter(new(QuotaResolutionKind.Policy, policy), 0));
    }
}
