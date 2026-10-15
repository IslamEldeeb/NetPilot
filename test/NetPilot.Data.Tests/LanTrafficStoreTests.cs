using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class LanTrafficStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"netpilot-lan-{Guid.NewGuid():N}");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("AA-BB-CC-DD-EE-01");
    private string DbPath => Path.Combine(_dir, "test.db");
    public LanTrafficStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task UsageExclusionsAndFlowStatus_SurviveReopenAndSharedHandles()
    {
        var instant = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var flow = new LanFlowCounter("192.168.1.20", "192.168.1.50", 8000);
        var status = new LocalTrafficStatus { IsConfigured = true, LastReadAtUtc = instant,
            GeneratedAtUtc = instant, UnavailableSinceUtc = instant, LastWarningAtUtc = instant };
        using (var db = new NetPilotDatabase(DbPath))
        {
            var usage = new LiteUsageStore(db);
            await usage.UpsertStateAsync(new() { Mac = Mac, CurrentDayBytes = 300, CurrentMonthBytes = 500,
                CurrentDayLanBytes = 2000, CurrentMonthLanBytes = 6000, PendingLanCarryBytes = 100,
                CurrentDayKey = "2026-10-15", CurrentMonthKey = "2026-10", LastPollAtUtc = instant,
                LastRawCounterBytes = 8500 }, Ct);
            await usage.AppendHistoryAsync(new(Mac, "2026-09", 300, instant, 600), Ct);
            await usage.AppendDailyHistoryAsync(new(Mac, "2026-10-14", 200, instant, 400), Ct);
            var local = new LiteLocalTrafficStore(db);
            await local.SaveFlowsAsync([flow], Ct);
            await local.SaveStatusAsync(status, Ct);
            await new LiteDeviceStore(db).UpsertAsync(new() { Mac = Mac, UsageLanOnly = true }, Ct);
        }
        using var reopened = new NetPilotDatabase(DbPath);
        using var shared = new NetPilotDatabase(DbPath);
        var loaded = new LiteUsageStore(reopened);
        var state = (await loaded.FindStateAsync(Mac, Ct))!;
        Assert.Equal(300, state.CurrentDayBytes);
        Assert.Equal(500, state.CurrentMonthBytes);
        Assert.Equal(2000, state.CurrentDayLanBytes);
        Assert.Equal(6000, state.CurrentMonthLanBytes);
        Assert.Equal(100, state.PendingLanCarryBytes);
        Assert.Equal(8500, state.LastRawCounterBytes);
        Assert.Equal(instant, state.LastPollAtUtc);
        Assert.Equal(600, Assert.Single(await loaded.GetAllHistoryAsync(Ct)).LanBytes);
        Assert.Equal(400, Assert.Single(await loaded.GetAllDailyHistoryAsync(Ct)).LanBytes);
        var localLoaded = new LiteLocalTrafficStore(reopened);
        Assert.Equal(flow, Assert.Single(await localLoaded.GetFlowsAsync(Ct)));
        Assert.Equal(status, await localLoaded.GetStatusAsync(Ct));
        Assert.True((await new LiteDeviceStore(reopened).FindByMacAsync(Mac, Ct))!.UsageLanOnly);
        await new LiteLocalTrafficStore(shared).SaveFlowsAsync([flow with { Bytes = 9000 }], Ct);
        Assert.Equal(9000, Assert.Single(await localLoaded.GetFlowsAsync(Ct)).Bytes);
        Assert.False(await localLoaded.TryRecordFailureAsync(instant.AddMinutes(59), Ct));
        var other = new LiteLocalTrafficStore(shared);
        Assert.True(await other.TryRecordFailureAsync(instant.AddHours(1), Ct));
        Assert.False(await localLoaded.TryRecordFailureAsync(instant.AddHours(1), Ct));
        // A late successful read must not undo the warning claim written by the other process.
        await localLoaded.SaveStatusAsync(status with { UnavailableSinceUtc = null }, Ct);
        Assert.False(await other.TryRecordFailureAsync(instant.AddHours(1), Ct));
    }

    [Fact]
    public async Task LegacyDocuments_LoadZeroExclusionsWithoutMigration()
    {
        using var db = new NetPilotDatabase(DbPath);
        db.GetCollection<BsonDocument>("usage_state").Insert(new BsonDocument {
            ["_id"] = Mac.ToString(), ["CurrentDayBytes"] = 500L, ["CurrentMonthBytes"] = 1000L });
        foreach (var collection in new[] { "usage_history", "usage_daily_history" })
            db.GetCollection<BsonDocument>(collection).Insert(new BsonDocument {
                ["_id"] = "legacy", ["Mac"] = Mac.ToString(), ["TotalBytes"] = 400L });
        db.GetCollection<BsonDocument>("devices").Insert(new BsonDocument { ["_id"] = Mac.ToString() });
        var usage = new LiteUsageStore(db);
        var state = (await usage.FindStateAsync(Mac, Ct))!;
        Assert.Equal(500, state.CurrentDayBytes);
        Assert.Equal(1000, state.CurrentMonthBytes);
        Assert.Equal(0, state.CurrentDayLanBytes);
        Assert.Equal(0, state.CurrentMonthLanBytes);
        Assert.Equal(0, state.PendingLanCarryBytes);
        Assert.Equal(0, Assert.Single(await usage.GetAllHistoryAsync(Ct)).LanBytes);
        Assert.Equal(0, Assert.Single(await usage.GetAllDailyHistoryAsync(Ct)).LanBytes);
        Assert.False((await new LiteDeviceStore(db).FindByMacAsync(Mac, Ct))!.UsageLanOnly);
        Assert.Empty(await new LiteLocalTrafficStore(db).GetFlowsAsync(Ct));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
