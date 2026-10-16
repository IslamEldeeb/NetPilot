using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetPilot.Abstractions;
using NetPilot.Agent;
using NetPilot.Core.Devices;
using NetPilot.Core.Health;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Rules;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Data.Tests;

public class RuleAgentIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-rule-agent-{Guid.NewGuid():N}");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("aa:bb:cc:dd:ee:01");
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    public RuleAgentIntegrationTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_RulesStoreFailureDoesNotSkipQuotaOrNextTick(bool timeout)
    {
        using var services = Services();
        var fixture = new Fixture(services);
        await fixture.SeedAsync();
        var failingStore = new FailOnceRuleStore(services.GetRequiredService<IRuleStore>(), timeout);
        var evaluator = fixture.Evaluator(failingStore);
        var worker = new Worker(NullLogger<Worker>.Instance, new ConfigurationBuilder().Build(), fixture.Policies,
            services.GetRequiredService<IRouterConnectionStore>(), services.GetRequiredService<RouterPasswordProtector>(),
            fixture.Provider, fixture.Reconciliation,
            new(fixture.Usage, fixture.Activity, fixture.Clock,
                new(new NullLocalTrafficSource(), services.GetRequiredService<ILocalTrafficStore>(), fixture.Activity, fixture.Clock), fixture.Devices),
            fixture.Quota, evaluator, services.GetRequiredService<NetPilot.Core.Health.IAgentHeartbeatStore>(), new(), new FixedTime());

        await worker.RunTickAsync(Ct);
        var stateId = QuotaPeriodState.Key(Mac, QuotaPeriodKind.Day, fixture.Clock.TodayKey());
        Assert.True((await services.GetRequiredService<IQuotaStateStore>().FindAsync(stateId, Ct))!.ExceededNotified);
        Assert.Empty(await fixture.Firings.GetAllAsync(Ct));
        fixture.Provider.Devices[0] = fixture.Provider.Devices[0] with { Usage = new(2000) };
        await worker.RunTickAsync(Ct);
        Assert.Equal(2000, (await fixture.Usage.FindStateAsync(Mac, Ct))!.CurrentDayBytes);
        Assert.Equal(2000, Assert.Single(await fixture.Firings.GetAllAsync(Ct)).UsedBytes);
        Assert.Single(fixture.Provider.AppliedLimits, w => w.Limit.DownloadKbps == 128);
    }

    [Fact]
    public async Task HeartbeatRecordsStartFailureAndRecoveryIncludingCaughtRouterWriteFailure()
    {
        using var services = Services();
        var fixture = new Fixture(services);
        await fixture.SeedAsync();
        await services.GetRequiredService<IRouterConnectionStore>().SaveAsync(
            new("fake", "fake-host", false, "admin", services.GetRequiredService<RouterPasswordProtector>().Encrypt("secret")), Ct);
        // Make reconciliation attempt a write, whose failure it catches internally.
        fixture.Provider.Devices[0] = fixture.Provider.Devices[0] with { CurrentLimit = new(true, 1, 1, null) };
        fixture.Provider.ThrowOnNextWrite = true;
        var heartbeatStore = new RecordingHeartbeatStore(services.GetRequiredService<IAgentHeartbeatStore>());
        var worker = fixture.Worker(heartbeatStore);
        await worker.RunHeartbeatTickAsync(Ct);
        Assert.Equal(2, heartbeatStore.Writes.Count);
        Assert.Null(heartbeatStore.Writes[0].LastTickCompletedUtc);
        var failed = (await heartbeatStore.GetAsync(Ct))!;
        Assert.Equal(Now, failed.LastTickStartedUtc);
        Assert.Equal(Now, failed.LastTickCompletedUtc);
        Assert.Equal(Now, failed.LastSuccessfulRouterReadUtc);
        Assert.Equal(1, failed.ConsecutiveFailedTicks);
        Assert.Equal("Router operation failed", failed.LastErrorSummary);
        Assert.Equal(new(true, true), failed.EffectiveModes);
        Assert.Equal(180, failed.TickIntervalSeconds);
        Assert.NotEmpty(failed.AppVersion);
        Assert.True(failed.LastTickDurationMs >= 0);
        await worker.RunHeartbeatTickAsync(Ct);
        var recovered = (await heartbeatStore.GetAsync(Ct))!;
        Assert.Equal(0, recovered.ConsecutiveFailedTicks);
        Assert.Null(recovered.LastErrorSummary);
        Assert.Equal(Now, recovered.LastSuccessfulRouterReadUtc);
    }

    [Fact]
    public async Task MissingConnectionCountsAsFailedTickAndHeartbeatWriteOutageDoesNotStopWork()
    {
        using var services = Services();
        var fixture = new Fixture(services);
        await fixture.SeedAsync();
        var heartbeatStore = new RecordingHeartbeatStore(services.GetRequiredService<IAgentHeartbeatStore>());
        var worker = fixture.Worker(heartbeatStore);
        await worker.RunHeartbeatTickAsync(Ct);
        await worker.RunHeartbeatTickAsync(Ct);
        var failed = (await heartbeatStore.GetAsync(Ct))!;
        Assert.Equal(2, failed.ConsecutiveFailedTicks);
        Assert.Equal("No router configured", failed.LastErrorSummary);
        Assert.Null(failed.LastSuccessfulRouterReadUtc);
        await services.GetRequiredService<IRouterConnectionStore>().SaveAsync(
            new("fake", "fake-host", false, "admin", services.GetRequiredService<RouterPasswordProtector>().Encrypt("secret")), Ct);
        heartbeatStore.FailWrites = true;
        await worker.RunHeartbeatTickAsync(Ct);
        Assert.NotEmpty(fixture.Provider.AppliedLimits);
        heartbeatStore.FailWrites = false;
        await worker.RunHeartbeatTickAsync(Ct);
        Assert.Equal(0, (await heartbeatStore.GetAsync(Ct))!.ConsecutiveFailedTicks);
    }

    [Fact]
    public async Task RouterReadFailurePreservesLastSuccessAndRedactsExceptionThenRecovers()
    {
        using var services = Services();
        var provider = new FailedReadProvider { FailRead = true };
        var fixture = new Fixture(services, provider);
        await fixture.SeedAsync();
        await services.GetRequiredService<IRouterConnectionStore>().SaveAsync(
            new("fake", "fake-host", false, "admin", services.GetRequiredService<RouterPasswordProtector>().Encrypt("secret")), Ct);
        var store = services.GetRequiredService<IAgentHeartbeatStore>();
        var oldRead = Now.AddMinutes(-5);
        await store.SaveAsync(new() { LastSuccessfulRouterReadUtc = oldRead }, Ct);
        // Exercise startup loading as well as tick execution; no timer sleeps.
        var worker = fixture.Worker(store);
        await worker.LoadHeartbeatAsync(Ct);
        await worker.RunHeartbeatTickAsync(Ct);
        var failed = (await store.GetAsync(Ct))!;
        Assert.Equal(oldRead, failed.LastSuccessfulRouterReadUtc);
        Assert.Equal("Router operation failed", failed.LastErrorSummary);
        Assert.Equal(1, failed.ConsecutiveFailedTicks);
        Assert.Equal(2000, failed.LastTickDurationMs);
        provider.FailRead = false;
        await worker.RunHeartbeatTickAsync(Ct);
        var recovered = (await store.GetAsync(Ct))!;
        Assert.Equal(Now, recovered.LastSuccessfulRouterReadUtc);
        Assert.Null(recovered.LastErrorSummary);
        Assert.Equal(0, recovered.ConsecutiveFailedTicks);
    }

    [Fact]
    public async Task RulesFailureStillCompletesHeartbeatWithSafeErrorAndThenRecovers()
    {
        using var services = Services();
        var fixture = new Fixture(services);
        await fixture.SeedAsync();
        await services.GetRequiredService<IRouterConnectionStore>().SaveAsync(
            new("fake", "fake-host", false, "admin", services.GetRequiredService<RouterPasswordProtector>().Encrypt("secret")), Ct);
        var store = services.GetRequiredService<IAgentHeartbeatStore>();
        var worker = fixture.Worker(store, new FailOnceRuleStore(services.GetRequiredService<IRuleStore>(), false));
        await worker.RunHeartbeatTickAsync(Ct);
        Assert.Equal("Rules evaluation failed", (await store.GetAsync(Ct))!.LastErrorSummary);
        Assert.Equal(1, (await store.GetAsync(Ct))!.ConsecutiveFailedTicks);
        await worker.RunHeartbeatTickAsync(Ct);
        Assert.Equal(0, (await store.GetAsync(Ct))!.ConsecutiveFailedTicks);
    }

    [Fact]
    public async Task EvaluatorAndDatabaseReopen_DoNotRefireOrReapply_AndDeletedRuleStillReleases()
    {
        var provider = new FakeRouterProvider(new() { SupportsSpeedLimit = true });
        Guid ruleId;
        using (var services = Services())
        {
            var fixture = new Fixture(services, provider);
            ruleId = await fixture.SeedAsync();
            await fixture.Evaluator().EvaluateAsync(provider, [], Ct);
            Assert.Single(provider.AppliedLimits);
        }
        using (var services = Services())
        {
            var fixture = new Fixture(services, provider);
            await fixture.Evaluator().EvaluateAsync(provider, [], Ct);
            Assert.Single(provider.AppliedLimits);
            Assert.Single(await fixture.Firings.GetAllAsync(Ct));
            await services.GetRequiredService<IRuleStore>().DeleteAsync(ruleId, Ct);
            await fixture.Evaluator().EvaluateAsync(provider, [], Ct);
            Assert.Empty(await services.GetRequiredService<IActionClaimStore>().GetAllAsync(Ct));
            Assert.Equal(5000, provider.AppliedLimits.Last().Limit.DownloadKbps);
            Assert.Equal(RuleFiringStatus.Reverted, Assert.Single(await fixture.Firings.GetAllAsync(Ct)).Status);
        }
    }

    private ServiceProvider Services() => new ServiceCollection()
        .AddNetPilotData(Path.Combine(_directory, "test.db"), Path.Combine(_directory, "keys")).BuildServiceProvider();

    private sealed class Fixture
    {
        private readonly ServiceProvider _services;
        public UsagePeriodClock Clock { get; } = new(timeProvider: new FixedTime());
        public IDeviceStore Devices { get; }
        public IUsageStore Usage { get; }
        public IPolicyStore Policies { get; }
        public IActivityLogStore Activity { get; }
        public IRuleFiringStore Firings { get; }
        public FakeRouterProvider Provider { get; }
        public PolicyReconciliationService Reconciliation { get; }
        public ClaimEnforcementService Claims { get; }
        public QuotaEnforcementService Quota { get; }
        public Fixture(ServiceProvider services, FakeRouterProvider? provider = null)
        {
            _services = services;
            Devices = services.GetRequiredService<IDeviceStore>(); Usage = services.GetRequiredService<IUsageStore>();
            Policies = services.GetRequiredService<IPolicyStore>(); Activity = services.GetRequiredService<IActivityLogStore>();
            Firings = services.GetRequiredService<IRuleFiringStore>();
            Provider = provider ?? new(new() { SupportsSpeedLimit = true });
            Reconciliation = new(Devices, Policies, Activity, new Classifier());
            Claims = new(services.GetRequiredService<IActionClaimStore>(), Devices, Policies, Reconciliation);
            Quota = new(Devices, Usage, services.GetRequiredService<IQuotaStore>(), services.GetRequiredService<IQuotaStateStore>(),
                services.GetRequiredService<IProtectedDeviceStore>(), Policies, services.GetRequiredService<IActionClaimStore>(), Claims, Activity, [], Clock);
        }
        public async Task<Guid> SeedAsync()
        {
            await Policies.UpsertAsync(new("mobile", new(true, 5000, 1000), 1, IsUserConfigured: true), Ct);
            await Devices.UpsertAsync(new() { Mac = Mac, Hostname = "Phone", CategoryKey = "mobile", FirstSeenAtUtc = Now.AddDays(-1),
                LastSeenAtUtc = Now, RouterReportedLimit = new(true, 5000, 1000, null) }, Ct);
            await Usage.UpsertStateAsync(new() { Mac = Mac, CurrentDayKey = Clock.TodayKey(), CurrentDayBytes = 1000,
                LastPollAtUtc = Now, LastRawCounterBytes = 1000 }, Ct);
            await _services.GetRequiredService<IQuotaStore>().UpsertAsync(new() { Id = "default", DailyBytes = 1000, DryRun = true }, Ct);
            var rule = new Rule { Name = "Live rule", CreatedAtUtc = Now.AddDays(-2), DryRun = false,
                Trigger = new UsageOver(RulePeriod.Day, 1000), Actions = [new Throttle(128)] };
            await _services.GetRequiredService<IRuleStore>().UpsertAsync(rule, Ct);
            Provider.Devices = [new(Mac, "192.168.1.5", "Phone", "mobile", new(ConnectionMedium.Wifi5Ghz, true),
                new(true, 5000, 1000, null), new(1000))];
            return rule.Id;
        }
        public Worker Worker(IAgentHeartbeatStore heartbeatStore, IRuleStore? rules = null) => new(
            NullLogger<Worker>.Instance, new ConfigurationBuilder().Build(), Policies,
            _services.GetRequiredService<IRouterConnectionStore>(), _services.GetRequiredService<RouterPasswordProtector>(),
            Provider, Reconciliation,
            new(Usage, Activity, Clock, new(new NullLocalTrafficSource(), _services.GetRequiredService<ILocalTrafficStore>(), Activity, Clock), Devices),
            Quota, Evaluator(rules), heartbeatStore, new() { ForceDryRun = true }, new FixedTime());

        public RuleEvaluationService Evaluator(IRuleStore? rules = null) => new(rules ?? _services.GetRequiredService<IRuleStore>(),
            Firings, Devices, Usage, _services.GetRequiredService<IProtectedDeviceStore>(), _services.GetRequiredService<IActionClaimStore>(), Claims,
            new([], _services.GetRequiredService<INotificationLogStore>(), Clock, new()), Activity, Clock, new());
    }
    private sealed class RecordingHeartbeatStore(IAgentHeartbeatStore inner) : IAgentHeartbeatStore
    {
        public List<AgentHeartbeat> Writes { get; } = [];
        public bool FailWrites { get; set; }
        public Task<AgentHeartbeat?> GetAsync(CancellationToken ct) => inner.GetAsync(ct);
        public Task SaveAsync(AgentHeartbeat heartbeat, CancellationToken ct)
        {
            if (FailWrites) throw new IOException("Secret text must not be logged or persisted");
            Writes.Add(heartbeat);
            return inner.SaveAsync(heartbeat, ct);
        }
    }
    private sealed class FixedTime : TimeProvider
    {
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _timestamp += 2000;
    }
    private sealed class FailedReadProvider() : FakeRouterProvider(new() { SupportsSpeedLimit = true }), IRouterProvider
    {
        public bool FailRead { get; set; }
        Task<IReadOnlyList<RouterDeviceSnapshot>> IRouterProvider.GetDevicesAsync(CancellationToken ct) =>
            FailRead ? throw new IOException("password=TOPSECRET; token=NEVER") : GetDevicesAsync(ct);
    }
    private sealed class Classifier : IDeviceClassifier { public string ClassifyKey(string hostname, MacAddress mac) => "mobile"; }
    private sealed class FailOnceRuleStore(IRuleStore inner, bool timeout) : IRuleStore
    {
        private bool _fail = true;
        public Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken ct)
        {
            if (!_fail) return inner.GetAllAsync(ct);
            _fail = false;
            if (timeout) throw new OperationCanceledException("Store timeout without caller cancellation");
            throw new IOException("Simulated rules store outage");
        }
        public Task<Rule?> FindAsync(Guid id, CancellationToken ct) => inner.FindAsync(id, ct);
        public Task UpsertAsync(Rule rule, CancellationToken ct) => inner.UpsertAsync(rule, ct);
        public Task DeleteAsync(Guid id, CancellationToken ct) => inner.DeleteAsync(id, ct);
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
