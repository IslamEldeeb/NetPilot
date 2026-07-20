using NetPilot.Core.Devices;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Usage;

public class UsageQueryTests
{
    private static readonly MacAddress MacA = new("AA-BB-CC-DD-EE-01");
    private static readonly MacAddress MacB = new("AA-BB-CC-DD-EE-02");

    private static readonly IReadOnlyList<DeviceUsageState> LiveStates =
    [
        new() { Mac = MacA, CurrentMonthKey = "2026-07", CurrentMonthBytes = 3000, CurrentDayKey = "2026-07-20", CurrentDayBytes = 200 },
        new() { Mac = MacB, CurrentMonthKey = "2026-07", CurrentMonthBytes = 1000, CurrentDayKey = "2026-07-20", CurrentDayBytes = 50 }
    ];

    private static readonly IReadOnlyList<UsageHistoryEntry> MonthlyHistory =
    [
        new(MacA, "2026-06", 9000, DateTimeOffset.UtcNow),
        new(MacB, "2026-06", 4000, DateTimeOffset.UtcNow)
    ];

    private static readonly IReadOnlyList<UsageDailyHistoryEntry> DailyHistory =
    [
        new(MacA, "2026-07-19", 600, DateTimeOffset.UtcNow),
        new(MacB, "2026-07-19", 300, DateTimeOffset.UtcNow)
    ];

    [Fact]
    public void CurrentMonth_UsesLiveState()
    {
        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Month, "2026-07", isCurrentPeriod: true,
            LiveStates, MonthlyHistory, DailyHistory);

        Assert.Equal(3000, result[(string)MacA]);
        Assert.Equal(1000, result[(string)MacB]);
    }

    [Fact]
    public void CurrentDay_UsesLiveState()
    {
        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Day, "2026-07-20", isCurrentPeriod: true,
            LiveStates, MonthlyHistory, DailyHistory);

        Assert.Equal(200, result[(string)MacA]);
        Assert.Equal(50, result[(string)MacB]);
    }

    [Fact]
    public void HistoricalMonth_UsesHistoryLookup_IgnoresLiveState()
    {
        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Month, "2026-06", isCurrentPeriod: false,
            LiveStates, MonthlyHistory, DailyHistory);

        Assert.Equal(9000, result[(string)MacA]);
        Assert.Equal(4000, result[(string)MacB]);
    }

    [Fact]
    public void HistoricalDay_UsesHistoryLookup_IgnoresLiveState()
    {
        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Day, "2026-07-19", isCurrentPeriod: false,
            LiveStates, MonthlyHistory, DailyHistory);

        Assert.Equal(600, result[(string)MacA]);
        Assert.Equal(300, result[(string)MacB]);
    }

    [Fact]
    public void HistoricalPeriod_NoMatchingEntries_ReturnsEmpty()
    {
        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Month, "2019-01", isCurrentPeriod: false,
            LiveStates, MonthlyHistory, DailyHistory);

        Assert.Empty(result);
    }

    [Fact]
    public void CurrentPeriod_StaleLiveState_FromEarlierPeriod_IsExcluded()
    {
        // Device went offline in June and hasn't been polled since — its live state is still
        // stamped with June's key. Querying the current month (July) must not count it.
        IReadOnlyList<DeviceUsageState> statesWithStaleDevice =
        [
            .. LiveStates,
            new() { Mac = new MacAddress("AA-BB-CC-DD-EE-03"), CurrentMonthKey = "2026-06", CurrentMonthBytes = 5_000_000_000, CurrentDayKey = "2026-06-30", CurrentDayBytes = 200_000 }
        ];

        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Month, "2026-07", isCurrentPeriod: true,
            statesWithStaleDevice, MonthlyHistory, DailyHistory);

        Assert.False(result.ContainsKey("AA-BB-CC-DD-EE-03"));
        Assert.Equal(3000, result[(string)MacA]);
        Assert.Equal(1000, result[(string)MacB]);
    }

    [Fact]
    public void DeviceScopedFilter_LooksUpSingleMacFromResult()
    {
        var result = UsageQuery.BytesByDevice(
            UsagePeriodType.Month, "2026-07", isCurrentPeriod: true,
            LiveStates, MonthlyHistory, DailyHistory);

        Assert.True(result.TryGetValue((string)MacA, out var bytes));
        Assert.Equal(3000, bytes);
        Assert.False(result.ContainsKey("FF-FF-FF-FF-FF-FF"));
    }
}
