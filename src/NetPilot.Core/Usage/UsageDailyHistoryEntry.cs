using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public record UsageDailyHistoryEntry(MacAddress Mac, string DayKey, long TotalBytes, DateTimeOffset FinalizedAtUtc);
