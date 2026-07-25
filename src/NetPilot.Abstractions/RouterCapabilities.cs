namespace NetPilot.Abstractions;

public record RouterCapabilities
{
    public bool SupportsSpeedLimit { get; init; }
    public bool SupportsDeviceCategorization { get; init; }
    public bool SupportsPriorityQos { get; init; }
    public bool SupportsGuestNetworkInfo { get; init; }
    public bool SupportsUsageTracking { get; init; }
    public bool SupportsReboot { get; init; }
    public bool SupportsWirelessRead { get; init; }
    public bool SupportsWirelessToggle { get; init; }
    public bool SupportsWirelessEdit { get; init; }
    public bool SupportsWirelessSchedule { get; init; }
    public bool SupportsDeviceBlocking { get; init; }
}
