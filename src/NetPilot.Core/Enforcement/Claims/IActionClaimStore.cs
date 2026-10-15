using NetPilot.Core.Devices;

namespace NetPilot.Core.Enforcement.Claims;

public interface IActionClaimStore
{
    Task<IReadOnlyList<ActionClaim>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<ActionClaim>> GetForDeviceAsync(MacAddress mac, CancellationToken ct);
    Task UpsertAsync(ActionClaim claim, CancellationToken ct);
    Task RemoveAsync(string id, CancellationToken ct);
    Task RemoveBySourceAsync(string source, CancellationToken ct);
    Task<ClaimDeviceState?> FindDeviceStateAsync(MacAddress mac, CancellationToken ct);
    Task UpsertDeviceStateAsync(ClaimDeviceState state, CancellationToken ct);
}
