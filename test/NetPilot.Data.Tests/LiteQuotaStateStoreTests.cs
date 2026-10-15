using LiteDB;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Quota;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class LiteQuotaStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-quota-state-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("aa:bb:cc:dd:ee:01");

    public LiteQuotaStateStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task PeriodStateAndReversalData_SurviveReopenAndUpsert()
    {
        var day = new QuotaPeriodState
        {
            Mac = Mac, PeriodKind = QuotaPeriodKind.Day, PeriodKey = "2026-10-15", WarnedPercents = [50, 80, 90],
            ExceededNotified = true, ActionKind = QuotaActionKind.Throttle,
            ActionAppliedAtUtc = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero),
            ExtraAllowanceBytes = 1_000_000_000, PriorLimit = new(true, 5000, 1000), AppliedThrottleKbps = 512,
            DryRunWouldHaveActed = true, ActionSkippedProtected = true, CapBytes = 5_000_000_000, UsedBytes = 6_000_000_000
        };
        var month = day with { PeriodKind = QuotaPeriodKind.Month, PeriodKey = "2026-10", ActionKind = QuotaActionKind.Block,
            BlockToken = "opaque-provider-token", PriorLimit = null, WarnedPercents = [] };
        using (var db = new NetPilotDatabase(DbPath))
        {
            var store = new LiteQuotaStateStore(db);
            Assert.Null(await store.FindAsync(day.Id, Ct));
            await store.UpsertAsync(day, Ct);
            await store.UpsertAsync(month, Ct);
            Assert.Equal("AA-BB-CC-DD-EE-01|day|2026-10-15", day.Id);
            Assert.Equal("AA-BB-CC-DD-EE-01|month|2026-10", month.Id);
        }
        using var reopened = new NetPilotDatabase(DbPath);
        var loaded = new LiteQuotaStateStore(reopened);
        Assert.Equal(2, (await loaded.ListWithActiveActionAsync(Ct)).Count);
        foreach (var expected in new[] { day, month })
        {
            var actual = await loaded.FindAsync(expected.Id, Ct);
            Assert.NotNull(actual);
            Assert.Equal(expected.WarnedPercents, actual.WarnedPercents);
            Assert.Equal(expected with { WarnedPercents = actual.WarnedPercents }, actual);
        }
        await loaded.UpsertAsync(month with { ActionKind = QuotaActionKind.None, ExtraAllowanceBytes = 2_000_000_000 }, Ct);
        Assert.Equal(day.Id, Assert.Single(await loaded.ListWithActiveActionAsync(Ct)).Id);
        Assert.Equal(2_000_000_000, (await loaded.FindAsync(month.Id, Ct))!.ExtraAllowanceBytes);
        Assert.Equal("opaque-provider-token", (await loaded.FindAsync(month.Id, Ct))!.BlockToken);
    }

    [Fact]
    public async Task PruningRemovesOnlyOldInactiveRows_IncludingMonths_AndRetainsBoundaryAndActiveTokens()
    {
        using var db = new NetPilotDatabase(DbPath);
        var store = new LiteQuotaStateStore(db);
        var old = new QuotaPeriodState { Mac = Mac, PeriodKind = QuotaPeriodKind.Day, PeriodKey = "2026-07-31" };
        var oldMonth = old with { PeriodKind = QuotaPeriodKind.Month, PeriodKey = "2026-07" };
        var active = old with { PeriodKey = "2026-07-30", ActionKind = QuotaActionKind.Block, BlockToken = "must-not-lose" };
        var boundary = old with { PeriodKey = "2026-08-14" };
        var recent = old with { PeriodKey = "2026-10-15" };
        foreach (var state in new[] { old, oldMonth, active, boundary, recent })
            await store.UpsertAsync(state, Ct);

        await store.PruneAsync(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero).AddDays(-62), Ct);

        Assert.Null(await store.FindAsync(old.Id, Ct));
        Assert.Null(await store.FindAsync(oldMonth.Id, Ct));
        Assert.NotNull(await store.FindAsync(boundary.Id, Ct));
        Assert.NotNull(await store.FindAsync(recent.Id, Ct));
        Assert.Equal("must-not-lose", Assert.Single(await store.ListWithActiveActionAsync(Ct)).BlockToken);
    }

    [Fact]
    public async Task QuotaThrottleAndUserOverride_ArePersistedSeparately_AndCanBeCleared()
    {
        var device = new Device { Mac = Mac, Override = new(true, 5000, 1000), QuotaThrottle = new(true, 512, 512) };
        using (var db = new NetPilotDatabase(DbPath))
            await new LiteDeviceStore(db).UpsertAsync(device, Ct);
        using var reopened = new NetPilotDatabase(DbPath);
        var store = new LiteDeviceStore(reopened);
        var actual = (await store.FindByMacAsync(Mac, Ct))!;
        Assert.Equal(device.Override, actual.Override);
        Assert.Equal(device.QuotaThrottle, actual.QuotaThrottle);
        actual.QuotaThrottle = null;
        await store.UpsertAsync(actual, Ct);
        actual = (await store.FindByMacAsync(Mac, Ct))!;
        Assert.Null(actual.QuotaThrottle);
        Assert.Equal(device.Override, actual.Override);
    }

    [Fact]
    public async Task ExistingDatabaseOpensWithoutStateCollection_AndLegacyDevicesHaveNoQuotaThrottle()
    {
        using (var legacy = new LiteDatabase(DbPath))
            legacy.GetCollection<BsonDocument>("devices").Insert(new BsonDocument
            {
                ["_id"] = (string)Mac, ["Hostname"] = "legacy-phone", ["ConnectionMedium"] = "Wifi5Ghz",
                ["OverrideEnabled"] = true, ["OverrideDownloadKbps"] = 5000, ["OverrideUploadKbps"] = 1000
            });
        using var db = new NetPilotDatabase(DbPath);
        var store = new LiteQuotaStateStore(db);
        Assert.Empty(await store.ListWithActiveActionAsync(Ct));
        var device = (await new LiteDeviceStore(db).FindByMacAsync(Mac, Ct))!;
        Assert.Null(device.QuotaThrottle);
        Assert.Equal(new SpeedLimit(true, 5000, 1000), device.Override);
        var state = new QuotaPeriodState { Mac = Mac, PeriodKind = QuotaPeriodKind.Day, PeriodKey = "2026-10-15" };
        await store.UpsertAsync(state, Ct);
        var loaded = (await store.FindAsync(state.Id, Ct))!;
        Assert.Equal(state with { WarnedPercents = loaded.WarnedPercents }, loaded);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
