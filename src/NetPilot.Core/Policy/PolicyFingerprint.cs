using System.Security.Cryptography;
using System.Text;
using NetPilot.Abstractions;

namespace NetPilot.Core.Policy;

/// <summary>
/// The whole "don't re-send limits that are already correct" mechanism: a cheap hash of
/// (CategoryKey, Override, PolicyDefinitionVersion). Only changes when the device is new,
/// its category changes, its override changes, or its category's policy is edited — a
/// strictly smaller trigger set than "every poll cycle".
/// </summary>
public static class PolicyFingerprint
{
    public static string Compute(string categoryKey, SpeedLimit? deviceOverride, int policyDefinitionVersion)
    {
        var input = $"{categoryKey}|{Describe(deviceOverride)}|{policyDefinitionVersion}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static string Describe(SpeedLimit? limit) =>
        limit is null ? "none" : $"{limit.Enabled}:{limit.DownloadKbps}:{limit.UploadKbps}";
}
