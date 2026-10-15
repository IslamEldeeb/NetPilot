using NetPilot.Abstractions;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Enforcement.Claims;

/// <summary>
/// Survives removal of the last claim and failed restores. PriorLimit is captured before the first
/// throttle write and retained until restoration succeeds. BlockToken proves ownership by claims;
/// Device.IsBlocked alone must never authorize an automatic unblock.
/// </summary>
public record ClaimDeviceState(MacAddress Mac, SpeedLimit? PriorLimit = null, string? BlockToken = null,
    bool UnblockProjectionPending = false);
