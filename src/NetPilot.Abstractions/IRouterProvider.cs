namespace NetPilot.Abstractions;

/// <summary>
/// The one seam every router brand implements. NetPilot.Core depends only on this —
/// never on a concrete router SDK.
/// </summary>
public interface IRouterProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    RouterCapabilities Capabilities { get; }

    Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct);

    Task<IReadOnlyList<RouterDeviceSnapshot>> GetDevicesAsync(CancellationToken ct);

    Task SetSpeedLimitAsync(string macAddress, SpeedLimit limit, CancellationToken ct);

    Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct);

    Task RebootAsync(CancellationToken ct);

    Task<IReadOnlyList<WirelessNetworkState>> GetWirelessNetworksAsync(CancellationToken ct);

    /// <summary>
    /// Applies a sparse update. Implementations MUST read current config and merge before
    /// writing if the underlying firmware only accepts whole-object writes (see phase3 capture
    /// W2). Returns the router's state after the write, re-read — not an echo of the request.
    /// </summary>
    Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkId id, WirelessNetworkUpdate update, CancellationToken ct);
}
