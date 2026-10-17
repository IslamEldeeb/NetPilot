using System.Globalization;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Devices;

/// <summary>
/// Turns a resolved quota policy into the plain-language trail the device detail page shows.
/// Pure so the wording is testable and independent of the Razor markup.
/// </summary>
public static class QuotaPolicyText
{
    public static string EffectivePolicy(Device device, QuotaResolution resolution)
    {
        if (resolution.Kind == QuotaResolutionKind.NotConfigured)
            return "No quota policy is set for this device yet.";
        if (resolution.Kind == QuotaResolutionKind.Exempt)
            return "This device is exempt from quotas. Usage is still tracked.";

        var policy = resolution.MatchedPolicy!;
        var scope = policy.Scope switch
        {
            QuotaScope.Device => $"Device override for {DeviceList.DisplayName(device)}",
            QuotaScope.Category => $"Inherits Category {Display(policy.CategoryKey)}",
            _ => "Inherits the global default"
        };
        var daily = policy.DailyBytes is { } day ? $"{Gb(day)} GB/day" : "no daily cap";
        var monthly = policy.MonthlyBytes is { } month ? $" + {Gb(month)} GB/month" : "";
        var action = policy.Action switch
        {
            QuotaAction.Throttle => $"Throttle {policy.ThrottleKbps?.ToString(CultureInfo.InvariantCulture)} Kbps",
            QuotaAction.Block => "Block Internet access",
            _ => "Notify only"
        };
        var mode = policy.DryRun ? ", dry-run" : "";
        return $"{scope} -> {daily}{monthly} -> {action}{mode}";
    }

    /// <summary>Today-versus-cap meter label, including the exact decimal-GB figures.</summary>
    public static string Meter(QuotaResolution resolution, long usedBytes)
    {
        if (resolution.Kind != QuotaResolutionKind.Policy)
            return resolution.Kind == QuotaResolutionKind.Exempt ? "Exempt" : "No daily cap";
        var cap = resolution.MatchedPolicy!.DailyBytes;
        if (cap is not > 0) return "No daily cap";
        var percent = Math.Clamp((decimal)usedBytes * 100m / cap.Value, 0, 100);
        return $"{DataSize.Format(usedBytes)} of {DataSize.Format(cap.Value)} ({percent.ToString("0.#", CultureInfo.InvariantCulture)}%)";
    }

    public static string MonthlyMeter(QuotaResolution resolution, long usedBytes)
    {
        if (resolution.Kind != QuotaResolutionKind.Policy)
            return resolution.Kind == QuotaResolutionKind.Exempt ? "Exempt" : "No monthly cap";
        var cap = resolution.MatchedPolicy!.MonthlyBytes;
        if (cap is not > 0) return "No monthly cap";
        var percent = Math.Clamp((decimal)usedBytes * 100m / cap.Value, 0, 100);
        return $"{DataSize.Format(usedBytes)} of {DataSize.Format(cap.Value)} ({percent.ToString("0.#", CultureInfo.InvariantCulture)}%)";
    }

    private static string Gb(long bytes) =>
        DataSize.ToGb(bytes).ToString("0.###", CultureInfo.InvariantCulture);

    private static string Display(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Unknown";
        return string.Join(' ', key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
