using NetPilot.Abstractions;

namespace NetPilot.Core.Settings;

public sealed class SettingsValidationException(string message) : Exception(message);

public static class RouterForms
{
    public static bool ValidHost(string host) => !string.IsNullOrWhiteSpace(host) && host.Length <= 253
        && Uri.TryCreate("http://" + host, UriKind.Absolute, out var uri) && uri.Host.Length > 0
        && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath == "/" && uri.Query == "" && uri.Fragment == ""
        && !host.Contains('/') && !host.Any(char.IsWhiteSpace);

    public static bool CanEdit(WirelessNetworkState network) => network.Id.Kind is WirelessNetworkKind.Guest or WirelessNetworkKind.Iot
        && (network.Features.CanToggle || network.Features.CanEditSsid);

    public static WirelessNetworkUpdate WirelessDelta(WirelessNetworkState current, bool enabled, string ssid, string password)
    {
        if (!CanEdit(current)) throw new SettingsValidationException("This network is read-only.");
        if (string.IsNullOrWhiteSpace(ssid) || ssid.Length > 32) throw new SettingsValidationException("SSID must be 1–32 characters.");
        return new()
        {
            Enabled = current.Features.CanToggle && current.Enabled != enabled ? enabled : null,
            Ssid = current.Features.CanEditSsid && current.Ssid != ssid ? ssid : null,
            Password = current.Id.Kind == WirelessNetworkKind.Guest && current.Features.CanEditPassword && password.Length > 0 ? password : null
        };
    }
}
