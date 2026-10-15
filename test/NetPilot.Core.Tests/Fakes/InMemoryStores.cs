using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Fakes;

public class InMemoryDeviceStore : IDeviceStore
{
    private readonly Dictionary<string, Device> _devices = [];

    public Task<Device?> FindByMacAsync(MacAddress mac, CancellationToken ct) =>
        Task.FromResult(_devices.GetValueOrDefault((string)mac));

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Device>>(_devices.Values.ToList());

    public Task UpsertAsync(Device device, CancellationToken ct)
    {
        _devices[(string)device.Mac] = device;
        return Task.CompletedTask;
    }
}

public class InMemoryPolicyStore : IPolicyStore
{
    private readonly Dictionary<string, DevicePolicy> _policies = [];

    public Task<DevicePolicy?> FindByCategoryAsync(string categoryKey, CancellationToken ct) =>
        Task.FromResult(_policies.GetValueOrDefault(categoryKey));

    public Task<IReadOnlyList<DevicePolicy>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DevicePolicy>>(_policies.Values.ToList());

    public Task UpsertAsync(DevicePolicy policy, CancellationToken ct)
    {
        _policies[policy.CategoryKey] = policy;
        return Task.CompletedTask;
    }

    public Task EnsureSeedCategoriesAsync(CancellationToken ct)
    {
        foreach (var category in DeviceCategory.SeedCategories)
            _policies.TryAdd(category.Key, new DevicePolicy(category.Key, Abstractions.SpeedLimit.Unlimited, 1, IsUserConfigured: false));
        return Task.CompletedTask;
    }
}

public class InMemoryActivityLogStore : IActivityLogStore
{
    public List<ActivityLogEntry> Entries { get; } = [];

    public Task AppendAsync(ActivityLogEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int count, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ActivityLogEntry>>(
            Entries.OrderByDescending(e => e.AtUtc).Take(count).ToList());
}

public class FixedCategoryClassifier(string categoryKey) : IDeviceClassifier
{
    public string ClassifyKey(string hostname, MacAddress mac) => categoryKey;
}

public class InMemoryUsageStore : IUsageStore
{
    private readonly Dictionary<string, DeviceUsageState> _states = [];
    private readonly Dictionary<string, UsageHistoryEntry> _history = [];
    private readonly Dictionary<string, UsageDailyHistoryEntry> _dailyHistory = [];

    public IReadOnlyList<UsageHistoryEntry> History => _history.Values.ToList();
    public IReadOnlyList<UsageDailyHistoryEntry> DailyHistory => _dailyHistory.Values.ToList();

    public Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct) =>
        Task.FromResult(_states.GetValueOrDefault((string)mac));

    public Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeviceUsageState>>(_states.Values.ToList());

    public Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct)
    {
        _states[(string)state.Mac] = state;
        return Task.CompletedTask;
    }

    public Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct)
    {
        _history[$"{entry.Mac}|{entry.MonthKey}"] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageHistoryEntry>> GetAllHistoryAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsageHistoryEntry>>(_history.Values.ToList());

    public Task AppendDailyHistoryAsync(UsageDailyHistoryEntry entry, CancellationToken ct)
    {
        _dailyHistory[$"{entry.Mac}|{entry.DayKey}"] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageDailyHistoryEntry>> GetAllDailyHistoryAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsageDailyHistoryEntry>>(_dailyHistory.Values.ToList());
}

public class InMemoryQuotaStore : IQuotaStore
{
    private readonly Dictionary<string, QuotaPolicy> _policies = [];

    public Task<IReadOnlyList<QuotaPolicy>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QuotaPolicy>>(_policies.Values.ToList());

    public Task<QuotaPolicy?> FindAsync(string id, CancellationToken ct) =>
        Task.FromResult(_policies.GetValueOrDefault(id));

    public Task UpsertAsync(QuotaPolicy policy, CancellationToken ct)
    {
        var errors = policy.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(policy));
        _policies[policy.Id] = policy;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        _policies.Remove(id);
        return Task.CompletedTask;
    }
}

public class InMemoryProtectedDeviceStore : IProtectedDeviceStore
{
    private readonly HashSet<MacAddress> _macs = [];

    public Task<IReadOnlyList<MacAddress>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MacAddress>>(_macs.ToList());

    public Task AddAsync(MacAddress mac, CancellationToken ct)
    {
        _macs.Add(mac);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(MacAddress mac, CancellationToken ct)
    {
        _macs.Remove(mac);
        return Task.CompletedTask;
    }

    public Task<bool> IsProtectedAsync(MacAddress mac, CancellationToken ct) => Task.FromResult(_macs.Contains(mac));
}

public class InMemoryQuotaStateStore : IQuotaStateStore
{
    private readonly Dictionary<string, QuotaPeriodState> _states = [];

    public Task<QuotaPeriodState?> FindAsync(string id, CancellationToken ct) =>
        Task.FromResult(_states.GetValueOrDefault(id));

    public Task UpsertAsync(QuotaPeriodState state, CancellationToken ct)
    {
        _states[state.Id] = state;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QuotaPeriodState>> ListWithActiveActionAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QuotaPeriodState>>(_states.Values.Where(s => s.ActionKind != QuotaActionKind.None).ToList());

    public Task PruneAsync(DateTimeOffset olderThanUtc, CancellationToken ct)
    {
        foreach (var state in _states.Values.Where(s => s.ActionKind == QuotaActionKind.None
            && DateTime.ParseExact(s.PeriodKey, s.PeriodKind == QuotaPeriodKind.Day ? "yyyy-MM-dd" : "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture) < olderThanUtc.UtcDateTime.Date).ToList())
            _states.Remove(state.Id);
        return Task.CompletedTask;
    }
}
