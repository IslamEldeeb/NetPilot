using Microsoft.Extensions.Logging;
using NetPilot.Abstractions;
using TpLink.Sdk;
using TpLink.Sdk.Models;

namespace NetPilot.Providers.TpLink;

/// <summary>
/// Thin adapter — translates TpLink.Sdk's models into NetPilot.Abstractions' shape. This is
/// the template every future brand's provider copies; NetPilot.Core never sees TpLink.Sdk directly.
/// </summary>
public class TpLinkRouterProvider(ILogger<TpLinkRouterProvider> logger) : IRouterProvider, IDisposable
{
    private TpLinkRouterClient? _client;
    private string _host = "";

    public string ProviderId => "tplink-archer-ax-series";
    public string DisplayName => "TP-Link Archer (AX-series)";

    public RouterCapabilities Capabilities { get; } = new(
        SupportsSpeedLimit: true,
        SupportsDeviceCategorization: true, // deviceType confirmed live — likely Fing-backed, see phase1-live-findings.md
        SupportsPriorityQos: false,         // enablePriority write path unconfirmed — phase1-live-findings.md "Remaining open items" #1
        SupportsGuestNetworkInfo: true,     // isGuest confirmed present on every device record
        SupportsUsageTracking: true);       // trafficUsage confirmed present on every device record, see phase2-usage-tracking-plan.md

    public async Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct)
    {
        _client?.Dispose();
        _host = settings.Host;
        _client = new TpLinkRouterClient(settings.Host, settings.UseHttps);
        await _client.LoginAsync(settings.Password, ct);
    }

    public async Task<IReadOnlyList<RouterDeviceSnapshot>> GetDevicesAsync(CancellationToken ct)
    {
        var records = await RequireClient().GetDevicesAsync(ct);
        return records.Select(ToSnapshot).ToList();
    }

    public Task SetSpeedLimitAsync(string macAddress, SpeedLimit limit, CancellationToken ct) =>
        RequireClient().SetSpeedLimitAsync(macAddress, limit.Enabled, limit.DownloadKbps, limit.UploadKbps, ct);

    /// <summary>
    /// Best-effort only: the model/firmware endpoint (admin/firmware?form=upgrade per
    /// NetPilot_Research_Findings_and_Architecture.md §3.3) was never live-verified against
    /// this firmware — only the Speed Limit path was, per the phased plan. Rather than guess
    /// its request/response shape, this returns what's already known (the configured host)
    /// and flags the rest as unconfirmed. Live-verify via Claude in Chrome before relying on
    /// model/firmware in the dashboard status panel.
    /// </summary>
    public Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct) =>
        Task.FromResult(new RouterInfo(Model: "unknown (unverified endpoint)", FirmwareVersion: "unknown (unverified endpoint)", Host: _host));

    private static ConnectionInfo ToConnectionInfo(TpLinkDeviceRecord record)
    {
        if (record.IsGuest)
            return new ConnectionInfo(ConnectionMedium.Guest, record.IsOnline);

        var medium = record.DeviceTag?.ToLowerInvariant() switch
        {
            "wired" => ConnectionMedium.Wired,
            "5g" => ConnectionMedium.Wifi5Ghz,
            "2.4g" or "iot_2.4g" => ConnectionMedium.Wifi24Ghz,
            _ => ConnectionMedium.Unknown
        };

        return new ConnectionInfo(medium, record.IsOnline);
    }

    private RouterDeviceSnapshot ToSnapshot(TpLinkDeviceRecord record) => new(
        MacAddress: record.Mac,
        IpAddress: record.Ip,
        Hostname: HasRealHostname(record.Host) ? record.Host : record.DeviceName ?? "",
        RawCategory: record.DeviceType,
        Connection: ToConnectionInfo(record),
        CurrentLimit: new SpeedLimitState(record.IsLimitEnabled, record.DownloadLimit, record.UploadLimit, record.SpeedLimitOnline),
        Usage: ToUsage(record));

    private UsageSnapshot? ToUsage(TpLinkDeviceRecord record)
    {
        if (!TpLinkUsageParser.TryParseBytes(record.TrafficUsageRaw, out var bytes))
        {
            if (record.TrafficUsageRaw is not null)
                logger.LogDebug("Could not parse trafficUsage {Raw} for {Mac}", record.TrafficUsageRaw, record.Mac);
            return null;
        }
        return new UsageSnapshot(bytes);
    }

    // Confirmed live: this firmware reports the literal string "NON_HOST" (not blank) for any
    // client that didn't send a DHCP hostname — router's own admin UI falls back to the
    // user-assigned deviceName alias in that case, so we do the same.
    private static bool HasRealHostname(string? host) =>
        !string.IsNullOrWhiteSpace(host) && !string.Equals(host, "NON_HOST", StringComparison.OrdinalIgnoreCase);

    private TpLinkRouterClient RequireClient() =>
        _client ?? throw new InvalidOperationException("ConnectAsync must be called before using this provider.");

    public void Dispose() => _client?.Dispose();
}
