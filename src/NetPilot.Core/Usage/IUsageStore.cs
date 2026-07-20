using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public interface IUsageStore
{
    Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct);
    Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct);
    Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct);

    Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<UsageHistoryEntry>> GetAllHistoryAsync(CancellationToken ct);

    Task AppendDailyHistoryAsync(UsageDailyHistoryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<UsageDailyHistoryEntry>> GetAllDailyHistoryAsync(CancellationToken ct);
}
