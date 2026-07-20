namespace NetPilot.Abstractions;

public record RouterCapabilities(
    bool SupportsSpeedLimit,
    bool SupportsDeviceCategorization,
    bool SupportsPriorityQos,
    bool SupportsGuestNetworkInfo,
    bool SupportsUsageTracking,
    bool SupportsReboot);
