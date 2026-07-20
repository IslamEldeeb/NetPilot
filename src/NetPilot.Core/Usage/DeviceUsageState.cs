using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public class DeviceUsageState
{
    public required MacAddress Mac { get; init; }
    public long LastRawCounterBytes { get; set; }
    public DateTimeOffset? LastPollAtUtc { get; set; }   // null = never observed yet
    public string CurrentMonthKey { get; set; } = "";
    public long CurrentMonthBytes { get; set; }
}
