using NetPilot.Abstractions;

namespace NetPilot.Core.Tests.Fakes;

public class FakeRouterProvider(RouterCapabilities capabilities) : IRouterProvider
{
    public List<RouterDeviceSnapshot> Devices { get; set; } = [];
    public List<(string Mac, SpeedLimit Limit)> AppliedLimits { get; } = [];
    public bool ThrowOnNextWrite { get; set; }

    public string ProviderId => "fake";
    public string DisplayName => "Fake Router";
    public RouterCapabilities Capabilities => capabilities;

    public Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct) => Task.CompletedTask;

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
}
