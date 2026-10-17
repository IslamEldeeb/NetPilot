using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Settings;

namespace NetPilot.Data.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-settings-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = default;
    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task OldOrPartialDocumentsUseDefaults_AndBothHostsSeeIndependentUpdates()
    {
        using var first = new NetPilotDatabase(DbPath); using var second = new NetPilotDatabase(DbPath);
        var web = new LitePreferencesStore(first); var agent = new LitePreferencesStore(second);
        Assert.Null(await web.GetNotificationsAsync(Ct)); Assert.Equal(new RetentionPreferences(), await web.GetRetentionAsync(Ct));
        first.GetCollection<BsonDocument>("ui_preferences").Insert(new BsonDocument { ["_id"] = "household" });
        Assert.Equal(new SetupProgress(), await web.GetAsync(Ct));
        await web.SaveNotificationsAsync(new() { QuietEnabled = true, DisabledEvents = [NotificationEvent.QuotaWarning] }, Ct);
        await agent.SaveRetentionAsync(new() { NotificationAttempts = 40, InactiveQuotaDays = 90 }, Ct);
        await web.SaveAsync(new(3), Ct);
        var saved = await agent.GetNotificationsAsync(Ct);
        Assert.True(saved!.Enabled); Assert.True(saved.QuietEnabled); Assert.Equal([NotificationEvent.QuotaWarning], saved.DisabledEvents);
        Assert.Equal(40, (await web.GetRetentionAsync(Ct)).NotificationAttempts); Assert.Equal(3, (await agent.GetAsync(Ct)).Step);
        await Assert.ThrowsAsync<ArgumentException>(() => web.SaveRetentionAsync(new() { InactiveQuotaDays = 1 }, Ct));
        Assert.Equal(90, (await agent.GetRetentionAsync(Ct)).InactiveQuotaDays);
    }

    [Fact]
    public async Task SetupResumesAcrossReopen_AndCreatesOnlyOneSafeQuotaWithoutOverwriting()
    {
        using (var db = new NetPilotDatabase(DbPath))
        {
            var setup = new LitePreferencesStore(db);
            await setup.SaveAsync(new(4), Ct);
            Assert.True(await setup.TryCreateDefaultQuotaAsync(5_000_000_000, Ct));
        }
        using var reopened = new NetPilotDatabase(DbPath);
        var resumed = new LitePreferencesStore(reopened);
        Assert.Equal(new SetupProgress(4), await resumed.GetAsync(Ct));
        var quota = (await new LiteQuotaStore(reopened).FindAsync("default", Ct))!;
        Assert.True(quota.DryRun); Assert.Equal(QuotaAction.NotifyOnly, quota.Action);
        Assert.False(await resumed.TryCreateDefaultQuotaAsync(10_000_000_000, Ct));
        Assert.Equal(5_000_000_000, (await new LiteQuotaStore(reopened).FindAsync("default", Ct))!.DailyBytes);
        var document = reopened.GetCollection<BsonDocument>("ui_preferences").FindById("household");
        Assert.Equal(new[] { "Completed", "Step" }, document["Setup"].AsDocument.Keys.Order().ToArray());
    }

    [Fact]
    public async Task RetentionPreservesActiveClaimsPendingQuotaMetadataAndActiveFirings()
    {
        using var db = new NetPilotDatabase(DbPath);
        var quotas = new LiteQuotaStateStore(db); var claims = new LiteActionClaimStore(db);
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        var pending = new QuotaPeriodState { Mac = mac, PeriodKind = QuotaPeriodKind.Day, PeriodKey = "2020-01-01", PriorLimit = new(false, null, null) };
        await quotas.UpsertAsync(pending, Ct);
        var active = pending with { PeriodKey = "2020-01-02", ActionKind = QuotaActionKind.Throttle, AppliedThrottleKbps = 512 };
        await quotas.UpsertAsync(active, Ct);
        var inactive = pending with { PeriodKey = "2020-01-03" }; await quotas.UpsertAsync(inactive, Ct);
        await claims.UpsertAsync(new(mac, ClaimKind.Throttle, "quota:day:2020-01-01", 512, DateTimeOffset.UtcNow), Ct);
        var firings = new LiteRuleFiringStore(db);
        var firing = new RuleFiring { RuleId = Guid.NewGuid(), Subject = mac, EpisodeKey = "episode", Status = RuleFiringStatus.Active };
        await firings.UpdateAsync(firing.Id, _ => firing, Ct);
        await quotas.PruneAsync(DateTimeOffset.UtcNow, Ct); await firings.PruneAsync(0, Ct);
        Assert.NotNull(await quotas.FindAsync(pending.Id, Ct)); Assert.NotNull(await quotas.FindAsync(active.Id, Ct));
        Assert.Null(await quotas.FindAsync(inactive.Id, Ct)); Assert.Single(await claims.GetAllAsync(Ct)); Assert.NotNull(await firings.FindAsync(firing.Id, Ct));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
