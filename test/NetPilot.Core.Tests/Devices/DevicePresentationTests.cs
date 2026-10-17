using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Tests;

public class DevicePresentationTests
{
    private static Device Device(string mac, string name, string category = "mobile", bool online = true,
        DateTimeOffset? firstSeen = null, DateTimeOffset? lastSeen = null) => new()
    {
        Mac = new(mac),
        FriendlyName = name,
        Hostname = "host-" + mac,
        IpAddress = "192.168.1.10",
        CategoryKey = category,
        Connection = new(Abstractions.ConnectionMedium.Wifi5Ghz, online),
        FirstSeenAtUtc = firstSeen ?? DateTimeOffset.UtcNow.AddDays(-10),
        LastSeenAtUtc = lastSeen ?? DateTimeOffset.UtcNow
    };

    private static readonly QuotaPolicy CategoryPolicy = new()
    {
        Id = "cat:mobile", Scope = QuotaScope.Category, CategoryKey = "mobile",
        DailyBytes = 5_000_000_000, MonthlyBytes = null, Action = QuotaAction.Throttle,
        ThrottleKbps = 512, DryRun = true
    };

    [Fact]
    public void DisplayName_FallsBackToHostnameThenMac()
    {
        Assert.Equal("Pixel", DeviceList.DisplayName(Device("AA-BB-CC-DD-EE-01", "Pixel")));
        var anonymous = new Device
        {
            Mac = new("AA-BB-CC-DD-EE-01"),
            FriendlyName = null,
            Hostname = "",
            FirstSeenAtUtc = DateTimeOffset.UtcNow
        };
        Assert.Equal("AA-BB-CC-DD-EE-01", DeviceList.DisplayName(anonymous));
    }

    [Fact]
    public void Build_ResolvesQuotaCapPercentAndStatusFlags()
    {
        var device = Device("AA-BB-CC-DD-EE-01", "Pixel");
        var rows = DeviceList.Build(new([device],
            new Dictionary<string, long> { ["AA-BB-CC-DD-EE-01"] = 2_500_000_000 },
            new Dictionary<string, long> { ["AA-BB-CC-DD-EE-01"] = 40_000_000_000 },
            [CategoryPolicy],
            [new ActionClaim(device.Mac, ClaimKind.Throttle, "rule:abc", 256, DateTimeOffset.UtcNow)],
            [device.Mac],
            DateTimeOffset.UtcNow));

        var row = Assert.Single(rows);
        Assert.True(row.HasQuota);
        Assert.Equal(5_000_000_000, row.QuotaDailyBytes);
        Assert.Equal(50m, row.QuotaPercent);
        Assert.True(row.IsThrottled);
        Assert.True(row.IsProtected);
        Assert.False(row.IsBlocked);
        Assert.False(row.IsNew);
    }

    [Fact]
    public void Apply_FiltersBySearchCategoryOnlineAndRestricted()
    {
        var online = Device("AA-BB-CC-DD-EE-01", "Pixel", online: true);
        var offline = Device("AA-BB-CC-DD-EE-02", "TV", category: "television", online: false);
        online.Override = new(true, 512, 512);
        offline.IsBlocked = true;
        var rows = DeviceList.Build(new([online, offline],
            new Dictionary<string, long>(), new Dictionary<string, long>(), [], [], [],
            DateTimeOffset.UtcNow));

        Assert.Single(DeviceList.Apply(rows, new(null, null, true, false, DeviceSortKey.Name, false)));
        Assert.Single(DeviceList.Apply(rows, new("TV", null, false, false, DeviceSortKey.Name, false)));
        Assert.Empty(DeviceList.Apply(rows, new("pixel", "television", false, false, DeviceSortKey.Name, false)));
        Assert.Equal(2, DeviceList.Apply(rows, new(null, null, false, true, DeviceSortKey.Name, false)).Count);
        Assert.Single(DeviceList.Apply(rows, new(null, "television", false, true, DeviceSortKey.Name, false)));
        Assert.Single(DeviceList.Apply(rows, new(null, null, true, true, DeviceSortKey.Name, false)));
        Assert.Equal(2, DeviceList.Apply(rows, new("192.168.1.10", null, false, false, DeviceSortKey.Name, false)).Count);
    }

    [Theory]
    [InlineData(DeviceSortKey.Name)]
    [InlineData(DeviceSortKey.LastSeen)]
    [InlineData(DeviceSortKey.UsageTodayDesc)]
    public void Apply_Sorts(DeviceSortKey sort)
    {
        var old = Device("AA-BB-CC-DD-EE-01", "Zed", lastSeen: DateTimeOffset.UtcNow.AddDays(-3));
        var recent = Device("AA-BB-CC-DD-EE-02", "Alpha", lastSeen: DateTimeOffset.UtcNow);
        var usage = new Dictionary<string, long> { ["AA-BB-CC-DD-EE-01"] = 1_000_000_000, ["AA-BB-CC-DD-EE-02"] = 9_000_000_000 };
        var rows = DeviceList.Build(new([old, recent], usage, new Dictionary<string, long>(), [], [], [], DateTimeOffset.UtcNow));

        var ordered = DeviceList.Apply(rows, new(null, null, false, false, sort, false)).Select(r => r.DisplayName).ToArray();
        Assert.Equal(["Alpha", "Zed"], ordered);
    }

    [Fact]
    public void Group_OrdersByCategoryKey()
    {
        var rows = DeviceList.Build(new(
            [Device("AA-BB-CC-DD-EE-01", "TV", "television"), Device("AA-BB-CC-DD-EE-02", "Pixel", "mobile")],
            new Dictionary<string, long>(), new Dictionary<string, long>(), [], [], [], DateTimeOffset.UtcNow));

        var groups = DeviceList.Group(rows);
        Assert.Equal(["mobile", "television"], groups.Select(g => g.Key));
    }

    [Fact]
    public void Counts_ReportsOnlineAndTotal()
    {
        var rows = DeviceList.Build(new(
            [Device("AA-BB-CC-DD-EE-01", "A", online: true), Device("AA-BB-CC-DD-EE-02", "B", online: false)],
            new Dictionary<string, long>(), new Dictionary<string, long>(), [], [], [], DateTimeOffset.UtcNow));

        Assert.Equal((1, 2), DeviceList.Counts(rows));
    }
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    public void NewBadge_RequiresFirstSeenWithinPast24Hours(int ageHours, bool expected)
    {
        var now = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        var device = Device("AA-BB-CC-DD-EE-01", "Phone", firstSeen: now.AddHours(-ageHours));
        var row = Assert.Single(DeviceList.Build(new([device], new Dictionary<string, long>(),
            new Dictionary<string, long>(), [], [], [], now)));
        Assert.Equal(expected, row.IsNew);
        Assert.True(DeviceList.MatchesSearch(row, "aa:bb:cc:dd:ee:01"));
    }

}
