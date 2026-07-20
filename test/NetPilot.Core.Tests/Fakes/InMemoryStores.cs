using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
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
            _policies.TryAdd(category.Key, new DevicePolicy(category.Key, Abstractions.SpeedLimit.Unlimited, 1));
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
    public List<UsageHistoryEntry> History { get; } = [];

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
        History.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageHistoryEntry>> GetHistoryAsync(MacAddress mac, int monthsBack, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsageHistoryEntry>>(
            History.Where(h => h.Mac == mac).OrderByDescending(h => h.MonthKey).Take(monthsBack).ToList());
}
