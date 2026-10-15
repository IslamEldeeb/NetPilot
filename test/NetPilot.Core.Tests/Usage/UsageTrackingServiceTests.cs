using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Usage;

public class UsageTrackingServiceTests
{
    private static RouterDeviceSnapshot MakeSnapshot(
        string mac = "AA-BB-CC-DD-EE-01", UsageSnapshot? usage = null) =>
        new(mac, "192.168.1.50", "phone-1", "Mobile",
            new ConnectionInfo(ConnectionMedium.Wifi24Ghz, true),
            new SpeedLimitState(false, null, null, null),
            usage);

    private static (UsageTrackingService Service, InMemoryUsageStore Usage, InMemoryActivityLogStore Log) Build(TimeProvider? timeProvider = null)
    {
        var usage = new InMemoryUsageStore();
        var log = new InMemoryActivityLogStore();
        var clock = new UsagePeriodClock(timeProvider: timeProvider ??
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero)));
        var service = new UsageTrackingService(usage, log, clock);
        return (service, usage, log);
    }

    [Fact]
    public async Task FirstObservation_CountsCurrentValueAsUsage_NoResetEventLogged()
    {
        var (service, usage, log) = Build();

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(500_000))], CancellationToken.None);

        var state = await usage.FindStateAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(500_000, state!.CurrentMonthBytes);
        Assert.Equal(500_000, state.CurrentDayBytes);
        Assert.Equal(500_000, state.LastRawCounterBytes);
        Assert.Empty(log.Entries); // nothing actually reset — this is just the first sighting
    }

    [Fact]
    public async Task CounterIncreases_AddsDeltaOnTopOfFirstObservation()
    {
        var (service, usage, _) = Build();

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1000))], CancellationToken.None); // counted in full: 1000
        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1500))], CancellationToken.None); // + delta 500

        var state = await usage.FindStateAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.Equal(1500, state!.CurrentMonthBytes);
        Assert.Equal(1500, state.CurrentDayBytes);
    }

    [Fact]
    public async Task CounterDecreases_TreatedAsReset_ResumesFromNewBaseline_LogsEvent()
    {
        var (service, usage, log) = Build();

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1000))], CancellationToken.None); // first sighting: counted in full, 1000
        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1500))], CancellationToken.None); // delta 500
        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(300))], CancellationToken.None);  // reset: add 300

        var state = await usage.FindStateAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.Equal(1800, state!.CurrentMonthBytes);
        Assert.Equal(1800, state.CurrentDayBytes);
        Assert.Single(log.Entries, e => e.Type == ActivityEventType.UsageCounterReset);
    }

    [Fact]
    public async Task MonthRollover_FinalizesPreviousMonth_ResetsRunningTotal()
    {
        var (service, usage, _) = Build();
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        await usage.UpsertStateAsync(new DeviceUsageState
        {
            Mac = mac,
            LastRawCounterBytes = 1000,
            LastPollAtUtc = new DateTimeOffset(2020, 1, 31, 12, 0, 0, TimeSpan.Zero),
            CurrentMonthKey = "2020-01",
            CurrentMonthBytes = 5000
        }, CancellationToken.None);

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1500))], CancellationToken.None);

        Assert.Single(usage.History, h => h.Mac == mac && h.MonthKey == "2020-01" && h.TotalBytes == 5000);

        var state = await usage.FindStateAsync(mac, CancellationToken.None);
        Assert.NotEqual("2020-01", state!.CurrentMonthKey);
        Assert.Equal(500, state.CurrentMonthBytes); // only the new month's delta (1500 - 1000)
    }

    [Theory]
    [InlineData(7, 21)]
    [InlineData(1, 22)]
    public async Task CairoDayRollover_FinalizesPreviousDay_ResetsRunningTotal(int month, int midnightUtcHour)
    {
        var midnightUtc = new DateTimeOffset(2026, month, 25, midnightUtcHour, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(midnightUtc.AddSeconds(-1));
        var (service, usage, log) = Build(time);
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1000))], CancellationToken.None);

        Assert.Empty(usage.DailyHistory);
        time.UtcNow = midnightUtc;

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1500))], CancellationToken.None);

        var history = Assert.Single(usage.DailyHistory);
        Assert.Equal(mac, history.Mac);
        Assert.Equal($"2026-{month:00}-25", history.DayKey);
        Assert.Equal(1000, history.TotalBytes);
        Assert.Equal(midnightUtc, history.FinalizedAtUtc);
        Assert.Equal(TimeSpan.Zero, history.FinalizedAtUtc.Offset);
        Assert.Empty(usage.History); // month didn't roll over — no monthly finalization

        var state = await usage.FindStateAsync(mac, CancellationToken.None);
        Assert.Equal($"2026-{month:00}-26", state!.CurrentDayKey);
        Assert.Equal(500, state.CurrentDayBytes); // only the new day's delta (1500 - 1000)
        Assert.Equal(1500, state.CurrentMonthBytes);
        Assert.Equal(midnightUtc, state.LastPollAtUtc);
        Assert.Equal(TimeSpan.Zero, state.LastPollAtUtc!.Value.Offset);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task SnapshotWithNullUsage_IsSkipped()
    {
        var (service, usage, _) = Build();

        await service.TrackAsync([MakeSnapshot(usage: null)], CancellationToken.None);

        var states = await usage.GetAllStatesAsync(CancellationToken.None);
        Assert.Empty(states);
    }
}
