using NetPilot.Abstractions;

namespace NetPilot.Core.Enforcement.Claims;

public record EffectiveRestriction(bool IsBlocked, SpeedLimit? Throttle)
{
    /// <summary>Block wins; otherwise the lowest symmetric Kbps cap wins. Empty means unrestricted.</summary>
    public static EffectiveRestriction Compute(IEnumerable<ActionClaim> claims)
    {
        var active = claims.Where(claim => claim.IsEffective).ToArray();
        if (active.Any(c => c.Kind == ClaimKind.Block))
            return new(true, null);
        var kbps = active.Where(c => c.Kind == ClaimKind.Throttle).Select(c => c.ThrottleKbps).Min();
        return new(false, kbps is { } cap ? new SpeedLimit(true, cap, cap) : null);
    }
}
