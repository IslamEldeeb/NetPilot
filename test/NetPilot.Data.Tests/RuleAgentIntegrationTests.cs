using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetPilot.Abstractions;
using NetPilot.Agent;
using NetPilot.Core.Devices;
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
            fixture.Quota, evaluator);

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
        public RuleEvaluationService Evaluator(IRuleStore? rules = null) => new(rules ?? _services.GetRequiredService<IRuleStore>(),
            Firings, Devices, Usage, _services.GetRequiredService<IProtectedDeviceStore>(), _services.GetRequiredService<IActionClaimStore>(), Claims,
            new([], _services.GetRequiredService<INotificationLogStore>(), Clock, new()), Activity, Clock, new());
    }
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
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
