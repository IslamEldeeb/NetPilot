using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Usage;

public class LocalTrafficTrackingTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string Server = "AA-BB-CC-DD-EE-01", Tv = "AA-BB-CC-DD-EE-02";
    private const string ServerIp = "192.168.1.20", TvIp = "192.168.1.50";

    [Fact]
    public async Task LongLivedFlows_BaselineOnly_ThenSubtractBothDirectionsFromBothEndpoints()
    {
        var h = new Harness();
        h.Flows(5000, 1000);
        await h.Track(100, 100);
        Assert.Equal(100, (await h.State(Tv)).CurrentDayBytes);
        h.Flows(7000, 1200);
        await h.Track(2300, 2600);
        var server = await h.State(Server);
        var tv = await h.State(Tv);
        Assert.Equal(100, server.CurrentDayBytes);
        Assert.Equal(400, tv.CurrentDayBytes);
        Assert.Equal(2200, server.CurrentDayLanBytes);
        Assert.Equal(2200, tv.CurrentMonthLanBytes);
        Assert.Empty(h.Log.Entries);
        Assert.Equal(h.Time.UtcNow, h.FlowStore.Status.LastReadAtUtc);
        Assert.Equal(h.Source.Reading!.GeneratedAtUtc, h.FlowStore.Status.GeneratedAtUtc);
    }

    [Fact]
    public async Task UnknownPeer_AttributesToKnownEndpoint_AndResetCountsNewValueOnce()
    {
        var h = new Harness();
        h.Source.Reading = new([new("192.168.1.99", TvIp, 500)], null);
        await h.Track(0, 0);
        h.Source.Reading = new([new("192.168.1.99", TvIp, 200)], null);
        await h.Track(1000, 1000);
        await h.Track(1000, 1000);
        Assert.Equal(800, (await h.State(Tv)).CurrentDayBytes);
        Assert.Equal(1000, (await h.State(Server)).CurrentDayBytes);
        Assert.Single(h.Log.Entries, e => e.Type == ActivityEventType.LanCounterReset);
    }

    [Theory]
    [InlineData(250, 150)]
    [InlineData(3_000_000_000, UsageTrackingService.MaxLanCarryBytes)]
    public async Task ExcessLan_ClampsAtZero_CapsCarry_AndSettlesAgainstNextRouterDelta(long flowDelta, long carry)
    {
        var h = new Harness();
        h.Flows(0);
        await h.Track(0, 0);
        h.Flows(flowDelta);
        await h.Track(100, 100);
        var tv = await h.State(Tv);
        Assert.Equal(0, tv.CurrentDayBytes);
        Assert.Equal(100, tv.CurrentDayLanBytes);
        Assert.Equal(carry, tv.PendingLanCarryBytes);
        // Recreate the tracker to prove carry and baselines are durable, not instance fields.
        h.NewService();
        await h.Track(100 + carry + 40, 100 + carry + 40);
        Assert.Equal(40, tv.CurrentDayBytes);
        Assert.Equal(100 + carry, tv.CurrentDayLanBytes);
        Assert.Equal(0, tv.PendingLanCarryBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceFailure_FallsBackWithoutConsumingCarryOrAdvancingBaseline_RateLimitsAcrossRestart(bool timeout)
    {
        var h = new Harness();
        h.Flows(0);
        await h.Track(0, 0);
        h.Flows(200);
        await h.Track(100, 100); // 100 bytes of carry
        h.Flows(500);
        h.Source.Failure = timeout ? new TaskCanceledException("secret-token-must-not-appear")
            : new HttpRequestException("secret-token-must-not-appear");
        await h.Track(200, 200);
        h.NewService();
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(59);
        await h.Track(300, 300);
        Assert.Equal(200, (await h.State(Tv)).CurrentDayBytes);
        Assert.Equal(100, (await h.State(Tv)).PendingLanCarryBytes);
        Assert.Equal(200, h.FlowStore.Flows[$"{ServerIp}|{TvIp}"].Bytes);
        Assert.Single(h.Log.Entries, e => e.Type == ActivityEventType.LanSourceUnavailable);
        Assert.Equal(h.Time.UtcNow.AddMinutes(-59), h.FlowStore.Status.UnavailableSinceUtc);
        h.Time.UtcNow = h.Time.UtcNow.AddMinutes(1);
        await h.Track(400, 400);
        Assert.Equal(2, h.Log.Entries.Count(e => e.Type == ActivityEventType.LanSourceUnavailable));
        Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("secret-token"));
        h.Source.Failure = null;
        await h.Track(900, 900); // Recovery subtracts 300 accumulated LAN + 100 carry.
        Assert.Equal(400, (await h.State(Tv)).CurrentDayBytes);
        Assert.Equal(500, (await h.State(Tv)).CurrentDayLanBytes);
        Assert.Null(h.FlowStore.Status.UnavailableSinceUtc);
    }

    [Fact]
    public async Task LanOnly_ExcludesAllRouterTrafficWithoutSource_AndToggleAppliesToFutureDeltas()
    {
        var h = new Harness();
        var server = new Device { Mac = new(Server), UsageLanOnly = true };
        await h.Devices.UpsertAsync(server, Ct);
        await h.Track(2000, 2300);
        Assert.Equal(0, (await h.State(Server)).CurrentDayBytes);
        Assert.Equal(2000, (await h.State(Server)).CurrentDayLanBytes);
        Assert.Equal(2300, (await h.State(Tv)).CurrentDayBytes);
        server.UsageLanOnly = false;
        await h.Track(2100, 2400);
        Assert.Equal(100, (await h.State(Server)).CurrentDayBytes);
        Assert.False(h.FlowStore.Status.IsConfigured);
    }

    [Fact]
    public async Task Rollover_PreservesLanHistory_AndQueryFiltersStalePeriods()
    {
        var h = new Harness();
        await h.Devices.UpsertAsync(new Device { Mac = new(Server), UsageLanOnly = true }, Ct);
        await h.Track(2000, 0);
        h.Time.UtcNow = h.Time.UtcNow.AddMonths(1).AddDays(1);
        await h.Track(2300, 0);
        Assert.Equal(2000, h.Usage.History.Single(e => e.Mac == new MacAddress(Server)).LanBytes);
        Assert.Equal(2000, h.Usage.DailyHistory.Single(e => e.Mac == new MacAddress(Server)).LanBytes);
        var states = await h.Usage.GetAllStatesAsync(Ct);
        var lan = UsageQuery.LanBytesByDevice(UsagePeriodType.Day, h.Clock.TodayKey(), true, states, [], []);
        Assert.Equal(300, lan[new MacAddress(Server)]);
        var history = UsageQuery.LanBytesByDevice(UsagePeriodType.Month, "2026-10", false, states, h.Usage.History, []);
        Assert.Equal(2000, history[new MacAddress(Server)]);
        Assert.Empty(UsageQuery.LanBytesByDevice(UsagePeriodType.Day, "2020-01-01", true, states, [], []));
    }

    [Fact]
    public async Task InvalidReading_DoesNotAdvanceAnyFlowBaseline()
    {
        var h = new Harness();
        h.Flows(100);
        await h.Track(0, 0);
        h.Source.Reading = new([new(ServerIp, TvIp, 200), new(ServerIp, TvIp, 300)], null);
        await h.Track(1000, 1000);
        Assert.Equal(100, h.FlowStore.Flows[$"{ServerIp}|{TvIp}"].Bytes);
        Assert.Equal(1000, (await h.State(Tv)).CurrentDayBytes);
        Assert.NotNull(h.FlowStore.Status.UnavailableSinceUtc);
    }

    private sealed class Harness
    {
        public InMemoryUsageStore Usage { get; } = new();
        public InMemoryActivityLogStore Log { get; } = new();
        public InMemoryLocalTrafficStore FlowStore { get; } = new();
        public InMemoryDeviceStore Devices { get; } = new();
        public FakeLocalTrafficSource Source { get; } = new();
        public FakeTimeProvider Time { get; } = new(new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        public UsagePeriodClock Clock { get; }
        private UsageTrackingService _service = null!;
        public Harness() { Clock = new(timeProvider: Time); NewService(); }
        public void NewService() => _service = new(Usage, Log, Clock, new(Source, FlowStore, Log, Clock), Devices);
        public void Flows(long forward, long reverse = 0) => Source.Reading = new(
            [new(ServerIp, TvIp, forward), new(TvIp, ServerIp, reverse)], Time.UtcNow);
        public Task Track(long server, long tv) => _service.TrackAsync([Snapshot(Server.ToLowerInvariant().Replace('-', ':'), ServerIp, server), Snapshot(Tv, TvIp, tv)], Ct);
        public async Task<DeviceUsageState> State(string mac) => (await Usage.FindStateAsync(new(mac), Ct))!;
        private static RouterDeviceSnapshot Snapshot(string mac, string ip, long bytes) => new(mac, ip, "test", "tv",
            new(ConnectionMedium.Wifi5Ghz, true), new(false, null, null, null), new(bytes));
    }
}
