using NetPilot.Abstractions;

namespace NetPilot.Core.Policy;

/// <summary>
/// The policy for one device category. DefinitionVersion is bumped on every edit — that
/// single counter is what invalidates every device fingerprint in the category at once.
/// IsUserConfigured distinguishes a policy a human actually saved from the Unlimited
/// fallback auto-created for a never-seen category — PolicyReconciliationService only ever
/// writes to the router for the former, so a fresh/empty local database can never
/// auto-push Unlimited over limits configured elsewhere (e.g. the deployed server).
/// </summary>
public record DevicePolicy(string CategoryKey, SpeedLimit Limit, int DefinitionVersion, bool IsUserConfigured)
{
    /// <summary>No-op (version unchanged) if newLimit is identical to the current one.</summary>
    public DevicePolicy WithLimit(SpeedLimit newLimit) => newLimit == Limit
        ? this
        : this with { Limit = newLimit, DefinitionVersion = DefinitionVersion + 1 };

    /// <summary>
    /// For policy rows written before IsUserConfigured existed (no such field in the stored
    /// document at all — see LitePolicyStore). Both auto-created fallback rows are always
    /// (LimitEnabled: false, DefinitionVersion: 1) — anything that deviates from that exact
    /// shape is provably the result of a human editing and saving through the dashboard.
    /// </summary>
    public static bool InferConfiguredFromLegacy(bool limitEnabled, int definitionVersion) =>
        limitEnabled || definitionVersion > 1;
}
