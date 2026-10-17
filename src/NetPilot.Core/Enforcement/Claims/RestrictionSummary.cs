using System.Globalization;

namespace NetPilot.Core.Enforcement.Claims;

/// <summary>Plain-words explanation of which claim wins, matching EffectiveRestriction.Compute.</summary>
public static class RestrictionSummary
{
    public static string Winner(IReadOnlyList<ActionClaim> claims)
    {
        if (claims.Any(c => c.Kind == ClaimKind.Block))
            return "A block is active — it wins over every throttle.";
        var throttle = claims.Where(c => c.Kind == ClaimKind.Throttle).Select(c => c.ThrottleKbps).Min();
        return throttle is { } kbps
            ? $"Lowest active cap wins: {kbps.ToString(CultureInfo.InvariantCulture)} Kbps in both directions."
            : "No active automatic throttle or block claims.";
    }

}
