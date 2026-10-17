using Microsoft.Extensions.DependencyInjection;
using NetPilot.Abstractions;
using NetPilot.Core.Actions;
using NetPilot.Core.Budget;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Data.Tests;

public sealed class BatchCStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-batch-c-{Guid.NewGuid():N}");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("AA-BB-CC-DD-EE-01");
    private readonly BatchCTimeProvider _time = new(DateTimeOffset.Parse("2026-10-16T12:00:00Z"));
    public BatchCStoreTests() => Directory.CreateDirectory(_directory);
    private ServiceProvider Services() => new ServiceCollection().AddNetPilotData(Path.Combine(_directory, "test.db"), Path.Combine(_directory, "keys"))
        .AddSingleton(new UsagePeriodClock(timeProvider: _time)).AddSingleton(new TemporaryActionOptions(false)).BuildServiceProvider();

    [Fact]
    public async Task BudgetAndTemporaryBlock_SurviveDatabaseReopen_AndExpiryUnblocksExactlyOnce()
    {
        var provider = new FakeRouterProvider(new());
        using (var services = Services())
        {
            await services.GetRequiredService<IHouseholdBudgetStore>().SaveAsync(new(300_000_000_001m, 31), Ct);
            await services.GetRequiredService<IDeviceStore>().UpsertAsync(new() { Mac = Mac, Hostname = "TV" }, Ct);
            await services.GetRequiredService<TemporaryActions>().RequestAsync(new(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false), Ct);
            await Tick(services, provider);
            Assert.Single(provider.BlockedMacs);
        }
        using (var services = Services())
        {
            Assert.Equal(new(300_000_000_001m, 31), await services.GetRequiredService<IHouseholdBudgetStore>().GetAsync(Ct));
            var claim = Assert.Single(await services.GetRequiredService<IActionClaimStore>().GetAllAsync(Ct));
            Assert.False(claim.Pending); Assert.False(claim.DryRun); Assert.NotNull(claim.ExpiresAtUtc);
            await Tick(services, provider);
            Assert.Single(provider.BlockedMacs);
            _time.UtcNow = _time.UtcNow.AddMinutes(15);
            await Tick(services, provider); await Tick(services, provider);
            Assert.Single(provider.UnblockRequests);
            Assert.Empty(await services.GetRequiredService<IActionClaimStore>().GetAllAsync(Ct));
            await services.GetRequiredService<IHouseholdBudgetStore>().SaveAsync(null, Ct);
            Assert.Null(await services.GetRequiredService<IHouseholdBudgetStore>().GetAsync(Ct));
        }
    }

    [Fact]
    public async Task OldDocuments_LoadWithNonexpiringLiveClaim_AndNoForecastEligibility()
    {
        using var services = Services();
        var db = services.GetRequiredService<NetPilotDatabase>();
        db.GetCollection<LiteDB.BsonDocument>("action_claims").Insert(new LiteDB.BsonDocument
        {
            ["_id"] = $"{Mac}|rule:old|Block", ["Mac"] = Mac.ToString(), ["Source"] = "rule:old",
            ["Kind"] = "Block", ["CreatedAtUtc"] = DateTime.UtcNow
        });
        var claim = Assert.Single(await services.GetRequiredService<IActionClaimStore>().GetAllAsync(Ct));
        Assert.True(claim.IsEffective); Assert.Null(claim.ExpiresAtUtc);
        db.GetCollection<LiteDB.BsonDocument>("usage_daily_history").Insert(new LiteDB.BsonDocument
        {
            ["_id"] = $"{Mac}|2026-10-15", ["Mac"] = Mac.ToString(), ["DayKey"] = "2026-10-15",
            ["TotalBytes"] = 100L, ["FinalizedAtUtc"] = DateTime.UtcNow
        });
        Assert.False(Assert.Single(await services.GetRequiredService<IUsageStore>().GetAllDailyHistoryAsync(Ct)).ForecastEligible);
        Assert.Null(await services.GetRequiredService<IHouseholdBudgetStore>().GetAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentRetries_DoNotDoubleBonusOrResetActiveClaim()
    {
        using var first = Services(); using var second = Services();
        await first.GetRequiredService<IDeviceStore>().UpsertAsync(new() { Mac = Mac }, Ct);
        await first.GetRequiredService<IQuotaStore>().UpsertAsync(new() { Id = "default", DailyBytes = 5_000_000_000 }, Ct);
        var clock = first.GetRequiredService<UsagePeriodClock>();
        var bonus = new QuotaBonusRequest(Guid.NewGuid(), Mac, QuotaPeriodKind.Day, clock.TodayKey(), 1_000_000_000);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() => (index % 2 == 0 ? first : second).GetRequiredService<QuotaBonusCommands>().GrantAsync(bonus, Ct))));
        var state = await first.GetRequiredService<IQuotaStateStore>().FindAsync(QuotaPeriodState.Key(Mac, QuotaPeriodKind.Day, clock.TodayKey()), Ct);
        Assert.Equal(1_000_000_000, state!.ExtraAllowanceBytes);
        var pause = new TemporaryRestrictionRequest(Guid.NewGuid(), [Mac], ClaimKind.Block, 15, DryRun: false);
        await first.GetRequiredService<TemporaryActions>().RequestAsync(pause, Ct);
        var store = first.GetRequiredService<IActionClaimStore>();
        var active = Assert.Single(await store.GetAllAsync(Ct)) with { Pending = false };
        await store.UpsertAsync(active, Ct);
        _time.UtcNow = _time.UtcNow.AddMinutes(1);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() => (index % 2 == 0 ? first : second).GetRequiredService<TemporaryActions>().RequestAsync(pause, Ct))));
        Assert.Equal(active, Assert.Single(await store.GetAllAsync(Ct)));
    }

    private static Task Tick(IServiceProvider services, FakeRouterProvider provider)
    {
        var devices = services.GetRequiredService<IDeviceStore>(); var claims = services.GetRequiredService<IActionClaimStore>();
        var policies = services.GetRequiredService<NetPilot.Core.Policy.IPolicyStore>();
        var reconciliation = new PolicyReconciliationService(devices, policies, services.GetRequiredService<IActivityLogStore>(), new HeuristicDeviceClassifier());
        return new TemporaryActionEnforcement(claims, devices, services.GetRequiredService<IProtectedDeviceStore>(),
            new(claims, devices, policies, reconciliation), services.GetRequiredService<UsagePeriodClock>(), new(false)).EvaluateAsync(provider, Ct);
    }
    public void Dispose() => Directory.Delete(_directory, true);
}

internal sealed class BatchCTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => UtcNow;
}
