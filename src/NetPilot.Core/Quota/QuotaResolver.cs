using NetPilot.Core.Devices;

namespace NetPilot.Core.Quota;

public enum QuotaResolutionKind { NotConfigured, Exempt, Policy }

/// <summary>The matched policy is retained for provenance, including an exempt match.</summary>
public record QuotaResolution(QuotaResolutionKind Kind, QuotaPolicy? MatchedPolicy);

/// <summary>
/// First match wins: device, category, default. An exempt match ends resolution.
/// A non-exempt policy with both caps null also wins: it explicitly means no cap at
/// that level, allowing a device to override a capped category without being exempt.
/// </summary>
public static class QuotaResolver
{
    public static QuotaResolution Resolve(Device device, IReadOnlyList<QuotaPolicy> policies)
    {
        var matched = policies.FirstOrDefault(p => p.Scope == QuotaScope.Device && p.Mac == device.Mac)
            ?? policies.FirstOrDefault(p => p.Scope == QuotaScope.Category && p.CategoryKey == device.CategoryKey)
            ?? policies.FirstOrDefault(p => p.Scope == QuotaScope.Default);
        return new(matched is null ? QuotaResolutionKind.NotConfigured
            : matched.Exempt ? QuotaResolutionKind.Exempt : QuotaResolutionKind.Policy, matched);
    }
}
