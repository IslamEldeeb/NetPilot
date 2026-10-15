using LiteDB;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NetPilot.Core.Notifications;
using NetPilot.Core.Usage;
using NetPilot.Data.Documents;

namespace NetPilot.Data.Tests;

public class NotificationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-notifications-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-15T12:00:00Z");
    public NotificationStoreTests() => Directory.CreateDirectory(_directory);
    private readonly List<ServiceProvider> _providers = [];
    private IDataProtectionProvider Protection()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_directory, "keys"))).SetApplicationName("NetPilot");
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public async Task ReSubscribeUpsertsByEndpoint_PreservesHistory_AndResultsAreVisibleFromSecondConnection()
    {
        using var firstDb = new NetPilotDatabase(DbPath);
        using var secondDb = new NetPilotDatabase(DbPath);
        var clock = new UsagePeriodClock("Africa/Cairo", new FixedTimeProvider());
        var first = new LitePushSubscriptionStore(firstDb, clock);
        var second = new LitePushSubscriptionStore(secondDb, clock);
        var subscription = new PushSubscriptionInfo("ignored-untrusted-id", "https://push.example/one", "p256dh", "auth", "Phone", Now.AddDays(-1));
        await first.UpsertAsync(subscription, Ct);
        var id = PushSubscriptionInfo.IdForEndpoint(subscription.Endpoint);
        await first.RecordResultAsync(id, true, Ct);
        await second.RecordResultAsync(id, false, Ct);
        await first.UpsertAsync(subscription with { Id = "other", Label = "Renamed", Auth = "new-auth", CreatedAtUtc = Now }, Ct);
        var loaded = Assert.Single(await second.GetAllAsync(Ct));
        Assert.Equal(id, loaded.Id);
        Assert.Equal("new-auth", loaded.Auth);
        Assert.Equal("Renamed", loaded.Label);
        Assert.Equal(subscription.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(Now, loaded.LastSuccessAtUtc);
        Assert.Equal(1, loaded.FailureCount);
        await second.RecordResultAsync(id, true, Ct);
        Assert.Equal(0, Assert.Single(await first.GetAllAsync(Ct)).FailureCount);
        await second.RemoveAsync(id, Ct);
        await first.RecordResultAsync(id, false, Ct);
        Assert.Empty(await first.GetAllAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentResultUpdatesDoNotLoseFailuresAcrossSharedConnections()
    {
        using var firstDb = new NetPilotDatabase(DbPath);
        using var secondDb = new NetPilotDatabase(DbPath);
        var first = new LitePushSubscriptionStore(firstDb, new UsagePeriodClock());
        var second = new LitePushSubscriptionStore(secondDb, new UsagePeriodClock());
        const string endpoint = "https://push.example/concurrent";
        var id = PushSubscriptionInfo.IdForEndpoint(endpoint);
        await first.UpsertAsync(new(id, endpoint, "p256dh", "auth", null, Now), Ct);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            (i % 2 == 0 ? first : second).RecordResultAsync(id, false, Ct))));
        Assert.Equal(20, Assert.Single(await second.GetAllAsync(Ct)).FailureCount);
    }

    [Fact]
    public async Task VapidKeysSurviveNewStoreAndKeyRingProvider_AndPrivateMaterialIsProtected()
    {
        VapidKeys original;
        using (var db = new NetPilotDatabase(DbPath))
        {
            original = await new VapidKeyStore(db, Protection()).GetOrCreateAsync(Ct);
            var raw = Assert.Single(db.GetCollection<BsonDocument>("vapid_keys").FindAll()).ToString();
            Assert.DoesNotContain(original.PrivateKey, raw);
            Assert.Contains(original.PublicKey, raw);
        }
        using var reopened = new NetPilotDatabase(DbPath);
        var loaded = await new VapidKeyStore(reopened, Protection()).GetOrCreateAsync(Ct);
        Assert.Equal(original.PublicKey, loaded.PublicKey);
        Assert.Equal(original.PrivateKey, loaded.PrivateKey);
        var publicBytes = Convert.FromBase64String(loaded.PublicKey.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal(65, publicBytes.Length);
        Assert.Equal(4, publicBytes[0]);
        Assert.DoesNotContain("=", loaded.PublicKey);
        Assert.Single(reopened.GetCollection<VapidKeyDocument>("vapid_keys").FindAll());
    }

    [Fact]
    public async Task ConcurrentFirstUseAcrossSharedConnectionsChoosesOneKeyPair()
    {
        using var firstDb = new NetPilotDatabase(DbPath);
        using var secondDb = new NetPilotDatabase(DbPath);
        var provider = Protection();
        var first = new VapidKeyStore(firstDb, provider);
        var second = new VapidKeyStore(secondDb, provider);
        using var start = new ManualResetEventSlim();
        var requests = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            start.Wait();
            return await (i % 2 == 0 ? first : second).GetOrCreateAsync(Ct);
        })).ToArray();
        start.Set();
        var keys = await Task.WhenAll(requests);
        Assert.Single(keys.Select(k => k.PublicKey).Distinct());
        Assert.Single(keys.Select(k => k.PrivateKey).Distinct());
        Assert.Single(firstDb.GetCollection<VapidKeyDocument>("vapid_keys").FindAll());
    }

    [Fact]
    public async Task LogRowsRoundTripAndPruningKeepsNewestTimestamps()
    {
        using var db = new NetPilotDatabase(DbPath);
        var store = new LiteNotificationLogStore(db);
        var entry = new NotificationLogEntry(Now, "quota:one", "Title", "webpush", NotificationOutcome.Failed, "HTTP 500");
        await store.AppendAsync(entry, Ct);
        Assert.Equal(entry, Assert.Single(await store.GetRecentAsync(1, Ct)));
        for (var i = 0; i < 505; i++) await store.AppendAsync(entry with { AtUtc = Now.AddSeconds(i + 1), Tag = i.ToString() }, Ct);
        await store.PruneAsync(500, Ct);
        using var secondDb = new NetPilotDatabase(DbPath);
        var recent = await new LiteNotificationLogStore(secondDb).GetRecentAsync(1000, Ct);
        Assert.Equal(500, recent.Count);
        Assert.Equal("504", recent[0].Tag);
        Assert.Equal("5", recent[^1].Tag);
    }

    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    public void Dispose()
    {
        foreach (var provider in _providers) provider.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
