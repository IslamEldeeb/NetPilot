using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Web;

// These are the existing Classic view direct actions; automatic enforcement stays in the Agent.
public sealed class DeviceRouterActions(
    IRouterProvider provider, IRouterConnectionStore connections, RouterPasswordProtector passwords,
    IDeviceStore devices, IActivityLogStore activity,
    PolicyReconciliationService reconciliation, UsageTrackingService usage)
{
    private async Task ConnectAsync()
    {
        var connection = await connections.GetAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("No router configured yet.");
        await provider.ConnectAsync(new RouterConnectionSettings(connection.Host, connection.UseHttps,
            connection.Username, passwords.Decrypt(connection.EncryptedPassword)), CancellationToken.None);
    }

    public async Task RefreshAsync()
    {
        await ConnectAsync();
        var snapshots = await reconciliation.ReconcileAsync(provider, CancellationToken.None);
        await usage.TrackAsync(snapshots, CancellationToken.None);
    }

    public async Task BlockAsync(MacAddress mac)
    {
        var device = await devices.FindByMacAsync(mac, CancellationToken.None)
            ?? throw new InvalidOperationException("Device no longer exists.");
        await ConnectAsync();
        var token = await provider.BlockDeviceAsync(mac, CancellationToken.None);
        device = await devices.FindByMacAsync(mac, CancellationToken.None) ?? device;
        device.IsBlocked = true;
        device.BlockListToken = token;
        await devices.UpsertAsync(device, CancellationToken.None);
        await activity.AppendAsync(new(DateTimeOffset.UtcNow, ActivityEventType.DeviceBlocked, mac,
            $"{DeviceList.DisplayName(device)} blocked from the dashboard"), CancellationToken.None);
    }

    public async Task UnblockAsync(MacAddress mac)
    {
        var device = await devices.FindByMacAsync(mac, CancellationToken.None)
            ?? throw new InvalidOperationException("Device no longer exists.");
        if (device.BlockListToken is null) throw new InvalidOperationException("No saved router token is available to unblock this device.");
        await ConnectAsync();
        await provider.UnblockDeviceAsync(mac, device.BlockListToken, CancellationToken.None);
        device = await devices.FindByMacAsync(mac, CancellationToken.None) ?? device;
        device.IsBlocked = false;
        device.BlockListToken = null;
        await devices.UpsertAsync(device, CancellationToken.None);
        await activity.AppendAsync(new(DateTimeOffset.UtcNow, ActivityEventType.DeviceUnblocked, mac,
            $"{DeviceList.DisplayName(device)} unblocked from the dashboard"), CancellationToken.None);
    }
}
