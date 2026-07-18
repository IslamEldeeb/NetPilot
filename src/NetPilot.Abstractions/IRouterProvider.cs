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
}
