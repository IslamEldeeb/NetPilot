using NetPilot.Abstractions;

namespace NetPilot.Agent;

// Observe even failures that reconciliation/rules catch internally, without changing their recovery behavior.
internal sealed class TickRouterProvider(IRouterProvider inner, TimeProvider clock) : IRouterProvider
{
    public DateTimeOffset? LastSuccessfulReadUtc { get; private set; }
    public string? ErrorSummary { get; private set; }
    public string ProviderId => inner.ProviderId;
    public string DisplayName => inner.DisplayName;
    public RouterCapabilities Capabilities => inner.Capabilities;

    public Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct) =>
        ObserveAsync(() => inner.ConnectAsync(settings, ct));
    public Task SetSpeedLimitAsync(string macAddress, SpeedLimit limit, CancellationToken ct) =>
        ObserveAsync(() => inner.SetSpeedLimitAsync(macAddress, limit, ct));
    public Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct) => ObserveAsync(() => inner.GetRouterInfoAsync(ct));
    public Task RebootAsync(CancellationToken ct) => ObserveAsync(() => inner.RebootAsync(ct));
    public Task<IReadOnlyList<WirelessNetworkState>> GetWirelessNetworksAsync(CancellationToken ct) =>
        ObserveAsync(() => inner.GetWirelessNetworksAsync(ct));
    public Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkId id, WirelessNetworkUpdate update, CancellationToken ct) =>
        ObserveAsync(() => inner.ApplyWirelessAsync(id, update, ct));
    public Task<string> BlockDeviceAsync(string macAddress, CancellationToken ct) => ObserveAsync(() => inner.BlockDeviceAsync(macAddress, ct));
    public Task UnblockDeviceAsync(string macAddress, string blockListToken, CancellationToken ct) =>
        ObserveAsync(() => inner.UnblockDeviceAsync(macAddress, blockListToken, ct));

    public async Task<IReadOnlyList<RouterDeviceSnapshot>> GetDevicesAsync(CancellationToken ct)
    {
        var snapshots = await ObserveAsync(() => inner.GetDevicesAsync(ct));
        LastSuccessfulReadUtc = clock.GetUtcNow();
        return snapshots;
    }

    private async Task ObserveAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception) { ErrorSummary = "Router operation failed"; throw; }
    }

    private async Task<T> ObserveAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Exception) { ErrorSummary = "Router operation failed"; throw; }
    }
}
