using NetPilot.Abstractions;

namespace NetPilot.Core.Devices;

/// <summary>Aggregate root. Identity is the MAC address — stable across router restarts and DHCP changes.</summary>
public class Device
{
    public required MacAddress Mac { get; init; }
    public string Hostname { get; set; } = "";
    public string? FriendlyName { get; set; }
    public string IpAddress { get; set; } = "";
    public string CategoryKey { get; set; } = DeviceCategory.UnknownKey;
    public ConnectionInfo Connection { get; set; } = new(ConnectionMedium.Unknown, IsOnline: false);
    public SpeedLimit? Override { get; set; }
    public bool IsBlocked { get; set; }

    /// <summary>Provider-specific token returned by IRouterProvider.BlockDeviceAsync — required
    /// to reverse the block later. See that method's remarks for why this can't be re-derived.</summary>
    public string? BlockListToken { get; set; }

    /// <summary>What the router itself last reported, independent of what NetPilot wants —
    /// refreshed every reconciliation tick regardless of whether a policy is enforced for
    /// this device. Null only before the device has ever been polled once.</summary>
    public SpeedLimitState? RouterReportedLimit { get; set; }

    public string? LastAppliedFingerprint { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
