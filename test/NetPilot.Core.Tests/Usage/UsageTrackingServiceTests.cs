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

    private static (UsageTrackingService Service, InMemoryUsageStore Usage, InMemoryActivityLogStore Log) Build()
    {
        var usage = new InMemoryUsageStore();
        var log = new InMemoryActivityLogStore();
        var service = new UsageTrackingService(usage, log);
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
            LastPollAtUtc = DateTimeOffset.UtcNow.AddMonths(-2),
            CurrentMonthKey = "2020-01",
            CurrentMonthBytes = 5000
        }, CancellationToken.None);

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1500))], CancellationToken.None);

        Assert.Single(usage.History, h => h.Mac == mac && h.MonthKey == "2020-01" && h.TotalBytes == 5000);

        var state = await usage.FindStateAsync(mac, CancellationToken.None);
        Assert.NotEqual("2020-01", state!.CurrentMonthKey);
        Assert.Equal(500, state.CurrentMonthBytes); // only the new month's delta (1500 - 1000)
    }

    [Fact]
    public async Task DayRollover_FinalizesPreviousDay_ResetsRunningTotal()
    {
        var (service, usage, _) = Build();
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        await usage.UpsertStateAsync(new DeviceUsageState
        {
            Mac = mac,
            LastRawCounterBytes = 1000,
            LastPollAtUtc = DateTimeOffset.UtcNow.AddDays(-3),
            CurrentMonthKey = DateTimeOffset.UtcNow.ToString("yyyy-MM"), // same month, so only the day rolls over
            CurrentMonthBytes = 9000,
            CurrentDayKey = "2020-01-01",
            CurrentDayBytes = 5000
        }, CancellationToken.None);

        await service.TrackAsync([MakeSnapshot(usage: new UsageSnapshot(1500))], CancellationToken.None);

        Assert.Single(usage.DailyHistory, h => h.Mac == mac && h.DayKey == "2020-01-01" && h.TotalBytes == 5000);
        Assert.Empty(usage.History); // month didn't roll over — no monthly finalization

        var state = await usage.FindStateAsync(mac, CancellationToken.None);
        Assert.NotEqual("2020-01-01", state!.CurrentDayKey);
        Assert.Equal(500, state.CurrentDayBytes); // only the new day's delta (1500 - 1000)
        Assert.Equal(9500, state.CurrentMonthBytes); // month total keeps accumulating (9000 + 500)
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
