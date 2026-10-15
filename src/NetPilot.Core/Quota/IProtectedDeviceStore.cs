using NetPilot.Core.Devices;

namespace NetPilot.Core.Quota;

/// <summary>
/// Protected devices are exempt from automatic Throttle and Block actions (enforced in T3).
/// Protection does not exempt them from usage tracking or quota notifications.
/// </summary>
public interface IProtectedDeviceStore
{
    Task<IReadOnlyList<MacAddress>> GetAllAsync(CancellationToken ct);
    Task AddAsync(MacAddress mac, CancellationToken ct);
    Task RemoveAsync(MacAddress mac, CancellationToken ct);
    Task<bool> IsProtectedAsync(MacAddress mac, CancellationToken ct);
}
