using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public interface IUsageStore
{
    Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct);
    Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct);
    Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct);
    Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<UsageHistoryEntry>> GetHistoryAsync(MacAddress mac, int monthsBack, CancellationToken ct);
}
