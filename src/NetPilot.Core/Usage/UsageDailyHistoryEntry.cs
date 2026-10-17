using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public record UsageDailyHistoryEntry(MacAddress Mac, string DayKey, long TotalBytes, DateTimeOffset FinalizedAtUtc, long LanBytes = 0, bool ForecastEligible = false);
