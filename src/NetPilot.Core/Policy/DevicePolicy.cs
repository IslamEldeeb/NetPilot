using NetPilot.Abstractions;

namespace NetPilot.Core.Policy;

/// <summary>
/// The policy for one device category. DefinitionVersion is bumped on every edit — that
/// single counter is what invalidates every device fingerprint in the category at once.
/// </summary>
public record DevicePolicy(string CategoryKey, SpeedLimit Limit, int DefinitionVersion)
{
    public DevicePolicy WithLimit(SpeedLimit newLimit) => this with
    {
        Limit = newLimit,
        DefinitionVersion = DefinitionVersion + 1
    };
}
