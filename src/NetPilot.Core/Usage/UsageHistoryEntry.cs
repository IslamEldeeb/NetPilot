using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public record UsageHistoryEntry(MacAddress Mac, string MonthKey, long TotalBytes, DateTimeOffset FinalizedAtUtc);
