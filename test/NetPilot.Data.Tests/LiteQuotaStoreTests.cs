using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Quota;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class LiteQuotaStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-quota-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;

    public LiteQuotaStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task AllScopesAndNullableFieldsSurviveReopening_ThenCanBeUpdatedAndDeleted()
    {
        QuotaPolicy[] policies =
        [
            new() { Id = "default", DailyBytes = 5_000_000_000, MonthlyBytes = null, WarnPercents = [20, 80, 99] },
            new() { Id = "cat:mobile", Scope = QuotaScope.Category, CategoryKey = "mobile", MonthlyBytes = 100_000_000_000,
                Action = QuotaAction.Throttle, ThrottleKbps = 512, DryRun = false, WarnPercents = [] },
            new() { Id = "dev:AA-BB-CC-DD-EE-01", Scope = QuotaScope.Device, Mac = new("aa:bb:cc:dd:ee:01"),
                Exempt = true, Action = QuotaAction.Block }
        ];
        using (var db = new NetPilotDatabase(DbPath))
        {
            var store = new LiteQuotaStore(db);
            foreach (var policy in policies)
                await store.UpsertAsync(policy, Ct);
        }
        using var reopened = new NetPilotDatabase(DbPath);
        var loadedStore = new LiteQuotaStore(reopened);
        Assert.Equal(3, (await loadedStore.GetAllAsync(Ct)).Count);
        foreach (var expected in policies)
        {
            var actual = await loadedStore.FindAsync(expected.Id, Ct);
            Assert.NotNull(actual);
            Assert.Equal(expected.WarnPercents, actual.WarnPercents);
            Assert.Equal(expected with { WarnPercents = actual.WarnPercents }, actual);
        }
        await loadedStore.UpsertAsync(policies[0] with { DailyBytes = null, MonthlyBytes = 20_000_000_000 }, Ct);
        Assert.Null((await loadedStore.FindAsync("default", Ct))!.DailyBytes);
        Assert.Equal(20_000_000_000, (await loadedStore.FindAsync("default", Ct))!.MonthlyBytes);
        await loadedStore.DeleteAsync("default", Ct);
        await loadedStore.DeleteAsync("default", Ct);
        Assert.Null(await loadedStore.FindAsync("default", Ct));
        Assert.Equal(2, (await loadedStore.GetAllAsync(Ct)).Count);
    }

    [Fact]
    public async Task InvalidQuotaCannotReplaceSavedPolicy()
    {
        using var db = new NetPilotDatabase(DbPath);
        var store = new LiteQuotaStore(db);
        await store.UpsertAsync(new() { Id = "default", DailyBytes = 100 }, Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(new() { Id = "default", DailyBytes = -1 }, Ct));
        Assert.Equal(100, (await store.FindAsync("default", Ct))!.DailyBytes);
    }

    [Fact]
    public async Task ProtectedMacsAreNormalizedIdempotentAndPersistAcrossReopen()
    {
        var mac = new MacAddress("aa:bb:cc:dd:ee:01");
        using (var db = new NetPilotDatabase(DbPath))
        {
            var store = new LiteProtectedDeviceStore(db);
            await store.AddAsync(mac, Ct);
            await store.AddAsync(new("AA-BB-CC-DD-EE-01"), Ct);
        }
        using var reopened = new NetPilotDatabase(DbPath);
        var loadedStore = new LiteProtectedDeviceStore(reopened);
        Assert.Equal(mac, Assert.Single(await loadedStore.GetAllAsync(Ct)));
        Assert.True(await loadedStore.IsProtectedAsync(mac, Ct));
        await loadedStore.RemoveAsync(mac, Ct);
        await loadedStore.RemoveAsync(mac, Ct);
        Assert.False(await loadedStore.IsProtectedAsync(mac, Ct));
        Assert.Empty(await loadedStore.GetAllAsync(Ct));
    }

    [Fact]
    public async Task ExistingDatabaseWithoutQuotaCollectionsOpensWithoutMigration()
    {
        using (var legacy = new LiteDatabase(DbPath))
            legacy.GetCollection<BsonDocument>("policies").Insert(new BsonDocument
            {
                ["_id"] = "mobile", ["LimitEnabled"] = false, ["DefinitionVersion"] = 1
            });
        using var db = new NetPilotDatabase(DbPath);
        var quotas = new LiteQuotaStore(db);
        var protectedDevices = new LiteProtectedDeviceStore(db);
        Assert.Empty(await quotas.GetAllAsync(Ct));
        Assert.Empty(await protectedDevices.GetAllAsync(Ct));
        Assert.NotNull(await new LitePolicyStore(db).FindByCategoryAsync("mobile", Ct));
        await quotas.UpsertAsync(new() { Id = "default" }, Ct);
        await protectedDevices.AddAsync(new("AA-BB-CC-DD-EE-01"), Ct);
        Assert.NotNull(await quotas.FindAsync("default", Ct));
        Assert.Single(await protectedDevices.GetAllAsync(Ct));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
