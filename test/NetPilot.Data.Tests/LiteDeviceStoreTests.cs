using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class LiteDeviceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-device-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;

    public LiteDeviceStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task LocalIdentityOverrides_RoundTripThroughReopen()
    {
        var device = new Device
        {
            Mac = new("aa:bb:cc:dd:ee:01"),
            Hostname = "tv",
            FriendlyName = "Living room TV",
            FriendlyNameLocked = true,
            CategoryKey = "laptop",
            RouterCategoryKey = "television",
            LocalCategoryKey = "laptop",
            LocalIconKey = "tv",
            FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-2)
        };

        using (var db = new NetPilotDatabase(DbPath))
            await new LiteDeviceStore(db).UpsertAsync(device, Ct);

        using var reopened = new NetPilotDatabase(DbPath);
        var loaded = await new LiteDeviceStore(reopened).FindByMacAsync(new("AA-BB-CC-DD-EE-01"), Ct);

        Assert.NotNull(loaded);
        Assert.Equal("Living room TV", loaded!.FriendlyName);
        Assert.True(loaded.FriendlyNameLocked);
        Assert.Equal("laptop", loaded.CategoryKey);
        Assert.Equal("television", loaded.RouterCategoryKey);
        Assert.Equal("laptop", loaded.LocalCategoryKey);
        Assert.Equal("tv", loaded.LocalIconKey);
    }

    [Fact]
    public async Task LegacyDocument_WithoutOverrideFields_LoadsWithInheritanceDefaults()
    {
        using (var db = new NetPilotDatabase(DbPath))
        {
            // Simulate a row written before local identity overrides existed.
            db.GetCollection<BsonDocument>("devices").Insert(new BsonDocument
            {
                ["_id"] = "AA-BB-CC-DD-EE-02", ["Hostname"] = "phone", ["CategoryKey"] = "mobile",
                ["ConnectionMedium"] = "Wifi5Ghz", ["IsOnline"] = true
            });
        }

        using var reopened = new NetPilotDatabase(DbPath);
        var loaded = await new LiteDeviceStore(reopened).FindByMacAsync(new("AA-BB-CC-DD-EE-02"), Ct);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.FriendlyName);
        Assert.False(loaded.FriendlyNameLocked);
        Assert.Equal("", loaded.RouterCategoryKey);
        Assert.Null(loaded.LocalCategoryKey);
        Assert.Null(loaded.LocalIconKey);
        Assert.Equal("mobile", loaded.CategoryKey);
    }

    [Fact]
    public async Task IdentityEditAndClear_SurviveStaleAgentUpsertsAcrossHandles()
    {
        using var agentDb = new NetPilotDatabase(DbPath);
        using var webDb = new NetPilotDatabase(DbPath);
        var agent = new LiteDeviceStore(agentDb);
        var web = new LiteDeviceStore(webDb);
        var mac = new MacAddress("AA-BB-CC-DD-EE-03");
        await agent.UpsertAsync(new Device { Mac = mac, Hostname = "router-name", CategoryKey = "mobile", RouterCategoryKey = "mobile" }, Ct);
        var stale = (await agent.FindByMacAsync(mac, Ct))!;
        await web.UpdateIdentityAsync(mac, new("My TV", "television", "tv"), Ct);
        stale.Hostname = "router-new";
        stale.RouterCategoryKey = "laptop";
        stale.CategoryKey = "laptop";
        await agent.UpsertAsync(stale, Ct);
        var edited = (await web.FindByMacAsync(mac, Ct))!;
        Assert.Equal("My TV", edited.FriendlyName);
        Assert.Equal("television", edited.CategoryKey);
        Assert.Equal("laptop", edited.RouterCategoryKey);
        Assert.Equal("router-new", edited.Hostname);
        Assert.Equal("tv", edited.LocalIconKey);
        await web.UpdateIdentityAsync(mac, new(null, null, null), Ct);
        await agent.UpsertAsync(edited, Ct);
        var cleared = (await web.FindByMacAsync(mac, Ct))!;
        Assert.Null(cleared.FriendlyName);
        Assert.False(cleared.FriendlyNameLocked);
        Assert.Null(cleared.LocalCategoryKey);
        Assert.Null(cleared.LocalIconKey);
        Assert.Equal("laptop", cleared.CategoryKey);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
