namespace NetPilot.Abstractions;

/// <summary>
/// Raw usage counter as reported by the router on this poll — cumulative, may reset
/// unpredictably (cause varies by router/firmware and NetPilot doesn't rely on knowing
/// which — see UsageTrackingService). Null on RouterDeviceSnapshot means either the
/// provider doesn't support usage tracking (check RouterCapabilities.SupportsUsageTracking
/// first) or it does but couldn't parse this particular reading.
/// </summary>
public record UsageSnapshot(long TotalBytes);
