using NetPilot.Core.Devices;

namespace NetPilot.Core.Enforcement;

public record ActivityLogEntry(DateTimeOffset AtUtc, ActivityEventType Type, MacAddress? Mac, string Message);
