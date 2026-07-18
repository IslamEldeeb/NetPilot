using NetPilot.Abstractions;

namespace NetPilot.Core.Devices;

/// <summary>Aggregate root. Identity is the MAC address — stable across router restarts and DHCP changes.</summary>
public class Device
{
    public required MacAddress Mac { get; init; }
    public string Hostname { get; set; } = "";
    public string? FriendlyName { get; set; }
    public string CategoryKey { get; set; } = DeviceCategory.UnknownKey;
    public ConnectionInfo Connection { get; set; } = new(ConnectionMedium.Unknown, IsOnline: false);
    public SpeedLimit? Override { get; set; }
    public string? LastAppliedFingerprint { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
