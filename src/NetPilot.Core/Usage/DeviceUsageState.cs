using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public class DeviceUsageState
{
    public required MacAddress Mac { get; init; }
    public long LastRawCounterBytes { get; set; }
    public DateTimeOffset? LastPollAtUtc { get; set; }   // null = never observed yet
    public string CurrentMonthKey { get; set; } = "";
    /// <summary>Internet bytes only. Quotas and rules consume this field unchanged.</summary>
    public long CurrentMonthBytes { get; set; }
    public string CurrentDayKey { get; set; } = "";
    /// <summary>Internet bytes only. Quotas and rules consume this field unchanged.</summary>
    public long CurrentDayBytes { get; set; }
    public long CurrentDayLanBytes { get; set; }
    public long CurrentMonthLanBytes { get; set; }
    public long PendingLanCarryBytes { get; set; }
}
