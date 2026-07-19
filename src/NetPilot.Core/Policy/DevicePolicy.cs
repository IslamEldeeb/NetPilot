using NetPilot.Abstractions;

namespace NetPilot.Core.Policy;

/// <summary>
/// The policy for one device category. DefinitionVersion is bumped on every edit — that
/// single counter is what invalidates every device fingerprint in the category at once.
/// </summary>
public record DevicePolicy(string CategoryKey, SpeedLimit Limit, int DefinitionVersion)
{
    /// <summary>No-op (version unchanged) if newLimit is identical to the current one.</summary>
    public DevicePolicy WithLimit(SpeedLimit newLimit) => newLimit == Limit
        ? this
        : this with { Limit = newLimit, DefinitionVersion = DefinitionVersion + 1 };
}
