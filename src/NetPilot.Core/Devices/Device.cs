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
    /// <summary>Effective throttle from all action claims; the name preserves policy fingerprints and persisted data.</summary>
    public SpeedLimit? QuotaThrottle { get; set; }
    /// <summary>Exclude all future router deltas, including any remote viewers, from Internet usage.</summary>
    public bool UsageLanOnly { get; set; }
    public bool IsBlocked { get; set; }

    /// <summary>Last category the router reported. Kept distinct from <see cref="CategoryKey"/>
    /// so a local category override can be shown next to the router's own value.</summary>
    public string RouterCategoryKey { get; set; } = "";

    /// <summary>Local category override chosen in the dashboard. Null inherits the router-reported
    /// category; when set it becomes the effective <see cref="CategoryKey"/> until cleared.</summary>
    public string? LocalCategoryKey { get; set; }

    /// <summary>True when <see cref="FriendlyName"/> is user-owned and must survive router snapshots.
    /// The Agent never writes a friendly name, but this keeps the guarantee explicit.</summary>
    public bool FriendlyNameLocked { get; set; }

    /// <summary>Optional local icon key; null derives the icon from the effective category.</summary>
    public string? LocalIconKey { get; set; }

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
