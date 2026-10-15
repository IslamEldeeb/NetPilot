using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Rules;

internal sealed class MemoryRuleStore : IRuleStore
{
    public Dictionary<Guid, Rule> Items { get; } = [];
    public Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Rule>>(Items.Values.ToArray());
    public Task<Rule?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task UpsertAsync(Rule rule, CancellationToken ct) { Items[rule.Id] = rule; return Task.CompletedTask; }
    public Task DeleteAsync(Guid id, CancellationToken ct) { Items.Remove(id); return Task.CompletedTask; }
}

internal sealed class MemoryFiringStore : IRuleFiringStore
{
    public Dictionary<string, RuleFiring> Items { get; } = [];
    private readonly Dictionary<string, RuleFiring> _latest = [];
    private readonly object _gate = new();
    public Task<IReadOnlyList<RuleFiring>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RuleFiring>>(Items.Values.ToArray());
    public Task<RuleFiring?> FindAsync(string id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
    public Task<RuleFiring?> FindLatestAsync(Guid id, string subject, CancellationToken ct) => Task.FromResult(_latest.GetValueOrDefault($"{id}|{subject}"));
    public Task<RuleFiring> UpdateAsync(string id, Func<RuleFiring?, RuleFiring> update, CancellationToken ct)
    {
        lock (_gate)
        {
            var value = update(Items.GetValueOrDefault(id));
            Items[id] = value;
            var key = $"{value.RuleId}|{value.Subject}";
            if (!_latest.TryGetValue(key, out var latest) || latest.FiredAtUtc <= value.FiredAtUtc) _latest[key] = value;
            return Task.FromResult(value);
        }
    }
    public Task<bool> RequestReleaseAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!Items.TryGetValue(id, out var current) || current.Status != RuleFiringStatus.Active)
                return Task.FromResult(false);
            UpdateAsync(id, row => row! with { ReleaseRequested = true }, ct);
            return Task.FromResult(true);
        }
    }
    public Task PruneAsync(int keepNewest, CancellationToken ct)
    {
        foreach (var item in Items.Values.OrderByDescending(f => f.FiredAtUtc).Skip(keepNewest).Where(f => f.Status != RuleFiringStatus.Active).ToArray())
            Items.Remove(item.Id);
        return Task.CompletedTask;
    }
}

internal sealed class RuleTestHarness
{
    public static readonly CancellationToken Ct = CancellationToken.None;
    public static readonly SpeedLimit Normal = new(true, 5000, 1000);
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-07-25T12:00:00Z"));
    public UsagePeriodClock Clock { get; }
    public InMemoryDeviceStore Devices { get; } = new();
    public InMemoryUsageStore Usage { get; } = new();
    public InMemoryPolicyStore Policies { get; } = new();
    public InMemoryProtectedDeviceStore Protected { get; } = new();
    public InMemoryActionClaimStore Claims { get; } = new();
    public InMemoryActivityLogStore Activity { get; } = new();
    public MemoryRuleStore Rules { get; } = new();
    public MemoryFiringStore Firings { get; } = new();
    public RulesOptions Options { get; } = new();
    public FakeRouterProvider Provider { get; } = new(new() { SupportsSpeedLimit = true });
    public RecordingChannel Channel { get; } = new();
    public NotificationLog NotificationHistory { get; } = new();
    public ClaimEnforcementService Enforcement { get; }
    public Device Device { get; private set; } = null!;

    private RuleTestHarness()
    {
        Clock = new(timeProvider: Time);
        var policy = new PolicyReconciliationService(Devices, Policies, Activity, new FixedCategoryClassifier("mobile"));
        Enforcement = new(Claims, Devices, Policies, policy);
    }
    public static async Task<RuleTestHarness> CreateAsync()
    {
        var h = new RuleTestHarness();
        await h.Policies.UpsertAsync(new("mobile", Normal, 1, IsUserConfigured: true), Ct);
        await h.Policies.UpsertAsync(new("tv", Normal, 1, IsUserConfigured: true), Ct);
        h.Device = await h.AddDeviceAsync(1);
        await h.SetUsageAsync(h.Device, 1000, 5000);
        return h;
    }
    public async Task<Device> AddDeviceAsync(int number, string category = "mobile")
    {
        var device = new Device { Mac = new($"aa:bb:cc:dd:ee:{number:x2}"), Hostname = $"Device {number}", CategoryKey = category,
            FirstSeenAtUtc = Time.UtcNow.AddDays(-1), LastSeenAtUtc = Time.UtcNow,
            RouterReportedLimit = new(true, 5000, 1000, null) };
        await Devices.UpsertAsync(device, Ct);
        return device;
    }
    public Task SetUsageAsync(Device device, long day, long month) => Usage.UpsertStateAsync(new()
    {
        Mac = device.Mac, CurrentDayKey = Clock.TodayKey(), CurrentMonthKey = Clock.ThisMonthKey(),
        CurrentDayBytes = day, CurrentMonthBytes = month
    }, Ct);
    public Rule Rule(RuleTrigger? trigger = null, params RuleAction[] actions) => new()
    {
        Name = "Test rule", DryRun = false, CreatedAtUtc = Time.UtcNow.AddDays(-2), UpdatedAtUtc = Time.UtcNow,
        Trigger = trigger ?? new UsageOver(RulePeriod.Day, 1000), Actions = actions.Length == 0 ? [new Notify("{rule}", "{device}: {used}/{cap} {unknown}")] : actions
    };
    public async Task<Rule> AddRuleAsync(Rule? rule = null)
    {
        rule ??= Rule();
        await Rules.UpsertAsync(rule, Ct);
        return rule;
    }
    public RuleEvaluationService NewEvaluator(IDeviceStore? devices = null) => new(Rules, Firings, devices ?? Devices, Usage, Protected, Claims, Enforcement,
        new([Channel], NotificationHistory, Clock, new()), Activity, Clock, Options);
    public Task TickAsync() => NewEvaluator().EvaluateAsync(Provider, [], Ct);
    public RuleFiring OnlyFiring => Assert.Single(Firings.Items.Values);
    public Task RequestReleaseAsync(RuleFiring? firing = null)
    {
        firing ??= OnlyFiring;
        return Firings.UpdateAsync(firing.Id, current => current! with { ReleaseRequested = true }, Ct);
    }
    public WirelessNetworkId AddWireless(bool enabled)
    {
        var id = WirelessNetworkId.Parse("guest-5g");
        Provider.WirelessNetworks[id] = new(id, "Guest", enabled, "Guest", true, null, null, null, null,
            new(true, false, false, false, false, false, false), []);
        return id;
    }
    public sealed class RecordingChannel : INotificationChannel
    {
        public string Id => "recording";
        public List<NotificationMessage> Messages { get; } = [];
        public Task SendAsync(NotificationMessage message, CancellationToken ct) { Messages.Add(message); return Task.CompletedTask; }
    }
    public sealed class NotificationLog : INotificationLogStore
    {
        public List<NotificationLogEntry> Entries { get; } = [];
        public Task AppendAsync(NotificationLogEntry entry, CancellationToken ct) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken ct) => Task.FromResult<IReadOnlyList<NotificationLogEntry>>(Entries.TakeLast(count).ToArray());
        public Task PruneAsync(int keepNewest, CancellationToken ct) => Task.CompletedTask;
    }
}
