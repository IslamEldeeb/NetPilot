using NetPilot.Abstractions;

namespace NetPilot.Core.Rules;

internal sealed class RuleActionDeferredException : OperationCanceledException;

// Count actual mutation attempts, including multiple writes made by claim reconciliation.
internal sealed class RuleRouterBudget(IRouterProvider inner, int remaining) : IRouterProvider
{
    public string ProviderId => inner.ProviderId;
    public string DisplayName => inner.DisplayName;
    public RouterCapabilities Capabilities => inner.Capabilities;
    public Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct) => inner.ConnectAsync(settings, ct);
    public Task<IReadOnlyList<RouterDeviceSnapshot>> GetDevicesAsync(CancellationToken ct) => inner.GetDevicesAsync(ct);
    public Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct) => inner.GetRouterInfoAsync(ct);
    public Task<IReadOnlyList<WirelessNetworkState>> GetWirelessNetworksAsync(CancellationToken ct) => inner.GetWirelessNetworksAsync(ct);

    public Task SetSpeedLimitAsync(string macAddress, SpeedLimit limit, CancellationToken ct)
    {
        Take();
        return inner.SetSpeedLimitAsync(macAddress, limit, ct);
    }
    public Task<string> BlockDeviceAsync(string macAddress, CancellationToken ct)
    {
        Take();
        return inner.BlockDeviceAsync(macAddress, ct);
    }
    public Task UnblockDeviceAsync(string macAddress, string blockListToken, CancellationToken ct)
    {
        Take();
        return inner.UnblockDeviceAsync(macAddress, blockListToken, ct);
    }
    public Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkId id, WirelessNetworkUpdate update, CancellationToken ct)
    {
        Take();
        return inner.ApplyWirelessAsync(id, update, ct);
    }
    public Task RebootAsync(CancellationToken ct)
    {
        Take();
        return inner.RebootAsync(ct);
    }

    public void RequireCapacity()
    {
        if (remaining == 0) throw new RuleActionDeferredException();
    }

    private void Take()
    {
        RequireCapacity();
        remaining--;
    }
}
