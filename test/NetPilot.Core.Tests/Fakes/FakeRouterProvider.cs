using NetPilot.Abstractions;

namespace NetPilot.Core.Tests.Fakes;

public class FakeRouterProvider(RouterCapabilities capabilities) : IRouterProvider
{
    public List<RouterDeviceSnapshot> Devices { get; set; } = [];
    public List<(string Mac, SpeedLimit Limit)> AppliedLimits { get; } = [];
    public bool ThrowOnNextWrite { get; set; }
    public Dictionary<WirelessNetworkId, WirelessNetworkState> WirelessNetworks { get; } = [];
    public List<(WirelessNetworkId Id, WirelessNetworkUpdate Update)> AppliedWirelessUpdates { get; } = [];
    public List<string> BlockedMacs { get; } = [];
    public List<string> UnblockedMacs { get; } = [];
    public int ConnectCallCount { get; private set; }
    public List<RouterConnectionSettings> ConnectCalls { get; } = [];
    public bool ThrowOnNextConnect { get; set; }

    public string ProviderId => "fake";
    public string DisplayName => "Fake Router";
    public RouterCapabilities Capabilities => capabilities;

    public Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct)
    {
        if (ThrowOnNextConnect)
        {
            ThrowOnNextConnect = false;
            throw new InvalidOperationException("Simulated connect failure.");
        }

        ConnectCallCount++;
        ConnectCalls.Add(settings);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RouterDeviceSnapshot>> GetDevicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RouterDeviceSnapshot>>(Devices);

    public Task SetSpeedLimitAsync(string macAddress, SpeedLimit limit, CancellationToken ct)
    {
        if (ThrowOnNextWrite)
        {
            ThrowOnNextWrite = false;
            throw new InvalidOperationException("Simulated write failure.");
        }

        AppliedLimits.Add((macAddress, limit));
        return Task.CompletedTask;
    }

    public Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct) =>
        Task.FromResult(new RouterInfo("Fake Model", "1.0", "fake-host"));

    public bool RebootCalled { get; private set; }

    public Task RebootAsync(CancellationToken ct)
    {
        RebootCalled = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WirelessNetworkState>> GetWirelessNetworksAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WirelessNetworkState>>(WirelessNetworks.Values.ToList());

    public Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkId id, WirelessNetworkUpdate update, CancellationToken ct)
    {
        if (ThrowOnNextWrite)
        {
            ThrowOnNextWrite = false;
            throw new InvalidOperationException("Simulated write failure.");
        }

        if (!WirelessNetworks.TryGetValue(id, out var current))
            throw new KeyNotFoundException($"No fake wireless network registered for '{id}'.");

        var updated = current with
        {
            Enabled = update.Enabled ?? current.Enabled,
            Ssid = update.Ssid ?? current.Ssid,
            SsidBroadcast = update.SsidBroadcast ?? current.SsidBroadcast,
            Channel = update.Channel ?? current.Channel,
            ChannelWidth = update.ChannelWidth ?? current.ChannelWidth,
            TxPower = update.TxPower ?? current.TxPower
        };

        WirelessNetworks[id] = updated;
        AppliedWirelessUpdates.Add((id, update));
        return Task.FromResult(updated);
    }

    public Task<string> BlockDeviceAsync(string macAddress, CancellationToken ct)
    {
        BlockedMacs.Add(macAddress);
        return Task.FromResult($"fake-token-{macAddress}");
    }

    public Task UnblockDeviceAsync(string macAddress, string blockListToken, CancellationToken ct)
    {
        UnblockedMacs.Add(macAddress);
        return Task.CompletedTask;
    }
}
