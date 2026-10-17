using NetPilot.Abstractions;
using NetPilot.Core.Enforcement;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Settings;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Web;

// Same direct paths as Classic view. Session ownership is deliberately deferred (D5).
public sealed class SettingsRouterActions(IRouterProvider provider, IRouterConnectionStore connections,
    RouterPasswordProtector passwords, IActivityLogStore activity, UsagePeriodClock clock)
{
    public async Task SaveAsync(string host, bool https, string password)
    {
        if (!RouterForms.ValidHost(host)) throw new SettingsValidationException("Enter a host or IP address without a scheme, path or credentials.");
        var prior = await connections.GetAsync(CancellationToken.None);
        if (password.Length == 0 && string.IsNullOrEmpty(prior?.EncryptedPassword)) throw new SettingsValidationException("Enter a router password for the first connection.");
        await connections.SaveAsync(new(provider.ProviderId, host.Trim(), https, prior?.Username ?? "admin",
            password.Length == 0 ? prior!.EncryptedPassword : passwords.Encrypt(password)), CancellationToken.None);
    }
    public async Task<RouterInfo> TestAsync(string host, bool https, string password)
    {
        if (!RouterForms.ValidHost(host)) throw new SettingsValidationException("Enter a host or IP address without a scheme, path or credentials.");
        var prior = await connections.GetAsync(CancellationToken.None);
        var secret = password.Length == 0 ? passwords.Decrypt(prior?.EncryptedPassword ?? throw new SettingsValidationException("Enter a password to test.")) : password;
        await provider.ConnectAsync(new(host.Trim(), https, prior?.Username ?? "admin", secret), CancellationToken.None);
        return await provider.GetRouterInfoAsync(CancellationToken.None);
    }
    private async Task ConnectSavedAsync()
    {
        var connection = await connections.GetAsync(CancellationToken.None) ?? throw new InvalidOperationException("Configure a router first.");
        await provider.ConnectAsync(new(connection.Host, connection.UseHttps, connection.Username,
            passwords.Decrypt(connection.EncryptedPassword)), CancellationToken.None);
    }
    public async Task<IReadOnlyList<WirelessNetworkState>> WirelessAsync()
    {
        if (!provider.Capabilities.SupportsWirelessRead) throw new NotSupportedException();
        await ConnectSavedAsync();
        return await provider.GetWirelessNetworksAsync(CancellationToken.None);
    }
    public async Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkState observed, bool enabled, string ssid, string password)
    {
        var delta = RouterForms.WirelessDelta(observed, enabled, ssid, password);
        if (delta.Enabled is not null && !provider.Capabilities.SupportsWirelessToggle
            || (delta.Ssid is not null || delta.Password is not null) && !provider.Capabilities.SupportsWirelessEdit)
            throw new SettingsValidationException("The provider does not support that wireless operation.");
        await ConnectSavedAsync();
        return await provider.ApplyWirelessAsync(observed.Id, delta, CancellationToken.None);
    }
    public async Task RebootAsync()
    {
        if (!provider.Capabilities.SupportsReboot) throw new NotSupportedException();
        await ConnectSavedAsync();
        await provider.RebootAsync(CancellationToken.None);
        await activity.AppendAsync(new(clock.GetUtcNow(), ActivityEventType.RouterRebooted, null,
            "Router reboot requested from Settings; recovery not verified"), CancellationToken.None);
    }
}
