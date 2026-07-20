namespace NetPilot.Abstractions;

/// <summary>One device's state as reported by a router provider on a single poll.</summary>
public record RouterDeviceSnapshot(
    string MacAddress,
    string IpAddress,
    string Hostname,
    string? RawCategory,
    ConnectionInfo Connection,
    SpeedLimitState CurrentLimit,
    UsageSnapshot? Usage);   // null if unsupported/unparseable this tick
