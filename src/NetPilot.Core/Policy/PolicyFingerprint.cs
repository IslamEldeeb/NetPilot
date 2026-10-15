using System.Security.Cryptography;
using System.Text;
using NetPilot.Abstractions;

namespace NetPilot.Core.Policy;

/// <summary>
/// The whole "don't re-send limits that are already correct" mechanism: a cheap hash of
/// (CategoryKey, Override, PolicyDefinitionVersion, QuotaThrottle). Only changes when the device is new,
/// its category, override or quota throttle changes, or its category's policy is edited — a
/// strictly smaller trigger set than "every poll cycle".
/// </summary>
public static class PolicyFingerprint
{
    public static string Compute(string categoryKey, SpeedLimit? deviceOverride, int policyDefinitionVersion, SpeedLimit? quotaThrottle = null)
    {
        // Preserve existing fingerprints for devices without a quota throttle.
        var input = $"{categoryKey}|{Describe(deviceOverride)}|{policyDefinitionVersion}";
        if (quotaThrottle is not null)
            input += $"|quota:{Describe(quotaThrottle)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static string Describe(SpeedLimit? limit) =>
        limit is null ? "none" : $"{limit.Enabled}:{limit.DownloadKbps}:{limit.UploadKbps}";
}
