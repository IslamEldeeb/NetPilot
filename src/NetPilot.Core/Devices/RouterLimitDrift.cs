using NetPilot.Abstractions;

namespace NetPilot.Core.Devices;

/// <summary>
/// Whether the router's actually-reported limit matches what NetPilot wants — display-only
/// (PolicyReconciliationService's own MatchesDesiredState governs enforcement, separately).
/// Two disabled limits are always equivalent regardless of leftover Kbps values — that's
/// what any "Unlimited" label already displays for either one, so a drift flag can never
/// disagree with its own text (e.g. "Router: Unlimited — expected Unlimited" shown as drift,
/// which happens if a category is toggled off but its Mbps boxes still hold old numbers).
/// </summary>
public static class RouterLimitDrift
{
    public static bool Matches(SpeedLimit reported, SpeedLimit desired) =>
        reported.Enabled == desired.Enabled
        && (!reported.Enabled || (reported.DownloadKbps == desired.DownloadKbps && reported.UploadKbps == desired.UploadKbps));
}
