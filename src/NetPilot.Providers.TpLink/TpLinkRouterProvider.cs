using Microsoft.Extensions.Logging;
using NetPilot.Abstractions;
using TpLink.Sdk;
using TpLink.Sdk.Models;
using TpLink.Sdk.Wireless;

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

    public RouterCapabilities Capabilities { get; } = new()
    {
        SupportsSpeedLimit = true,
        SupportsDeviceCategorization = true, // deviceType confirmed live — likely Fing-backed, see phase1-live-findings.md
        SupportsPriorityQos = false,         // enablePriority write path unconfirmed — phase1-live-findings.md "Remaining open items" #1
        SupportsGuestNetworkInfo = true,     // isGuest confirmed present on every device record
        SupportsUsageTracking = true,        // trafficUsage confirmed present on every device record, see phase2-usage-tracking-plan.md
        SupportsReboot = true,               // best-effort — endpoint unconfirmed, see TpLinkRouterClient.RebootAsync
        // Wireless reads confirmed live and plain-JSON per docs/phase3-live-findings.md.
        // Writes are confirmed live for guest (own capture) and IoT enable/SSID/broadcast (real
        // user DevTools capture) — main is not, and IoT password writes aren't either. These
        // flags are necessarily coarse: true means "at least one network kind/field supports
        // it," and ApplyWirelessAsync itself gates per WirelessNetworkKind/field. See there.
        SupportsWirelessRead = true,
        SupportsWirelessToggle = true,
        SupportsWirelessEdit = true,
        SupportsWirelessSchedule = false,
        // Confirmed live via a user-captured curl, not this session's own testing — see
        // docs/phase4-block-list-live-findings.md. Insert/remove work; there is no confirmed
        // read/list path for the blacklist, so NetPilot cannot reconcile blocked state against
        // the router the way it does for speed limits — it's a direct action, not a policy.
        SupportsDeviceBlocking = true
    };

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

    /// <summary>Confirmed live per docs/phase3-live-findings.md (R6) — admin/firmware?form=upgrade.</summary>
    public async Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct)
    {
        var info = await RequireClient().GetFirmwareInfoAsync(ct);
        return new RouterInfo(
            Model: info?.Model ?? "unknown (empty response)",
            FirmwareVersion: info?.FirmwareVersion ?? "unknown (empty response)",
            Host: _host);
    }

    public Task RebootAsync(CancellationToken ct) =>
        RequireClient().RebootAsync(ct);

    public Task<string> BlockDeviceAsync(string macAddress, CancellationToken ct) =>
        RequireClient().BlockDeviceAsync(macAddress, ct);

    public Task UnblockDeviceAsync(string macAddress, string blockListToken, CancellationToken ct) =>
        RequireClient().UnblockDeviceAsync(blockListToken, ct);

    /// <summary>
    /// Confirmed live per docs/phase3-live-findings.md. Four reads (main 2.4G, main 5G, both
    /// IoT bands in one call, both guest bands in one call) map onto six WirelessNetworkStates.
    /// CoupledWith is deliberately left empty for every network: the live capture confirmed
    /// IoT/guest have no channel/txpower fields of their own (strong structural evidence they
    /// ride the main band's radio), but that's not the same as confirming the W4 toggle-coupling
    /// behavior (does disabling main-2.4g also drop iot-2.4g?) — that capture never ran, so
    /// CoupledWith stays unpopulated rather than encoding an inference as a confirmed fact.
    /// </summary>
    public async Task<IReadOnlyList<WirelessNetworkState>> GetWirelessNetworksAsync(CancellationToken ct)
    {
        var client = RequireClient();
        var main2g = client.GetWireless2GAsync(ct);
        var main5g = client.GetWireless5GAsync(ct);
        var iot = client.GetWirelessIotAsync(ct);
        var guest = client.GetWirelessGuestAsync(ct);
        await Task.WhenAll(main2g, main5g, iot, guest);

        return
        [
            ToMainState(WirelessNetworkKind.Main, WirelessBand.Ghz24, "2.4 GHz", main2g.Result),
            ToMainState(WirelessNetworkKind.Main, WirelessBand.Ghz5, "5 GHz", main5g.Result),
            ToIotState(WirelessBand.Ghz24, IotDisplayName(WirelessBand.Ghz24), iot.Result),
            ToIotState(WirelessBand.Ghz5, IotDisplayName(WirelessBand.Ghz5), iot.Result),
            ToGuestState(WirelessBand.Ghz24, GuestDisplayName(WirelessBand.Ghz24), guest.Result),
            ToGuestState(WirelessBand.Ghz5, GuestDisplayName(WirelessBand.Ghz5), guest.Result)
        ];
    }

    /// <summary>
    /// Confirmed live for GUEST and IOT networks per docs/phase3-live-findings.md; MAIN still
    /// throws — its write shape was never captured (Smart Connect pairs 2.4G/5G under one SSID,
    /// so its password/SSID routing is a genuinely different question, not extrapolatable from
    /// guest or IoT), and main touches every device on the household network, unlike guest
    /// (zero clients) or this IoT capture (user-initiated, real DevTools traffic). Channel/width/
    /// TX power are rejected for every network — never captured for any of them. IoT password
    /// writes are also rejected: the one real IoT write capture always carried `encryption`
    /// alongside `psk_key`, and whether that's required or coincidental was never isolated
    /// (unlike guest, where a password-only write was independently tested and confirmed).
    /// </summary>
    public async Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkId id, WirelessNetworkUpdate update, CancellationToken ct)
    {
        if (id.Kind == WirelessNetworkKind.Main)
            throw new NotSupportedException(
                "Wireless write for main networks is not implemented — Smart Connect's shared SSID/password across bands was never captured live. See docs/phase3-live-findings.md.");

        if (update.Channel is not null || update.ChannelWidth is not null || update.TxPower is not null)
            throw new NotSupportedException(
                "Channel/channel-width/TX-power writes are not implemented for any network — never captured live. See docs/phase3-live-findings.md.");

        if (id.Kind == WirelessNetworkKind.Iot && update.Password is not null)
            throw new NotSupportedException(
                "IoT password writes are not implemented — untested whether 'encryption' must accompany 'psk_key'. See docs/phase3-live-findings.md.");

        var hidden = update.SsidBroadcast is null ? (bool?)null : !update.SsidBroadcast.Value;
        var client = RequireClient();

        if (id.Kind == WirelessNetworkKind.Guest)
        {
            var config = id.Band == WirelessBand.Ghz24
                ? await client.WriteWirelessGuest2GAsync(update.Enabled, update.Ssid, hidden, update.Password, ct)
                : await client.WriteWirelessGuest5GAsync(update.Enabled, update.Ssid, hidden, update.Password, ct);
            return ToGuestState(id.Band, GuestDisplayName(id.Band), config);
        }
        else
        {
            var config = id.Band == WirelessBand.Ghz24
                ? await client.WriteWirelessIot2GAsync(update.Enabled, update.Ssid, hidden, null, ct)
                : await client.WriteWirelessIot5GAsync(update.Enabled, update.Ssid, hidden, null, ct);
            return ToIotState(id.Band, IotDisplayName(id.Band), config);
        }
    }

    private static string GuestDisplayName(WirelessBand band) => band == WirelessBand.Ghz24 ? "Guest 2.4 GHz" : "Guest 5 GHz";
    private static string IotDisplayName(WirelessBand band) => band == WirelessBand.Ghz24 ? "IoT 2.4 GHz" : "IoT 5 GHz";

    private static WirelessNetworkState ToMainState(WirelessNetworkKind kind, WirelessBand band, string displayName, TpLinkMainBandConfig config) =>
        new(
            Id: new WirelessNetworkId(kind, band),
            DisplayName: displayName,
            Enabled: config.IsEnabled,
            Ssid: config.Ssid,
            SsidBroadcast: config.IsBroadcasting,
            SecurityMode: config.Encryption,
            Channel: config.CurrentChannel,
            ChannelWidth: config.ChannelWidth,
            TxPower: config.TxPower,
            Features: new WirelessNetworkFeatures(
                CanToggle: true, CanEditSsid: true, CanEditPassword: true,
                CanEditChannel: true, CanEditChannelWidth: true, CanEditTxPower: true, CanEditBroadcast: true),
            CoupledWith: []);

    private static WirelessNetworkState ToIotState(WirelessBand band, string displayName, TpLinkIotConfig config)
    {
        var (enabled, ssid, broadcasting) = band == WirelessBand.Ghz24
            ? (config.Is2gEnabled, config.Iot2gSsid, config.Is2gBroadcasting)
            : (config.Is5gEnabled, config.Iot5gSsid, config.Is5gBroadcasting);

        return new WirelessNetworkState(
            Id: new WirelessNetworkId(WirelessNetworkKind.Iot, band),
            DisplayName: displayName,
            Enabled: enabled,
            Ssid: ssid,
            SsidBroadcast: broadcasting,
            SecurityMode: band == WirelessBand.Ghz24 ? config.Iot2gEncryption : config.Iot5gEncryption,
            Channel: null,       // confirmed live: no channel field on IoT — rides the main band's radio
            ChannelWidth: null,
            TxPower: null,
            Features: new WirelessNetworkFeatures(
                CanToggle: true, CanEditSsid: true, CanEditPassword: true,
                CanEditChannel: false, CanEditChannelWidth: false, CanEditTxPower: false, CanEditBroadcast: true),
            CoupledWith: []);
    }

    private static WirelessNetworkState ToGuestState(WirelessBand band, string displayName, TpLinkGuestConfig config)
    {
        var (enabled, ssid, broadcasting) = band == WirelessBand.Ghz24
            ? (config.Is2gEnabled, config.Guest2gSsid, config.Is2gBroadcasting)
            : (config.Is5gEnabled, config.Guest5gSsid, config.Is5gBroadcasting);

        return new WirelessNetworkState(
            Id: new WirelessNetworkId(WirelessNetworkKind.Guest, band),
            DisplayName: displayName,
            Enabled: enabled,
            Ssid: ssid,
            SsidBroadcast: broadcasting,
            SecurityMode: band == WirelessBand.Ghz24 ? config.Guest2gEncryption : config.Guest5gEncryption,
            Channel: null,       // confirmed live: no channel field on Guest — rides the main band's radio
            ChannelWidth: null,
            TxPower: null,
            Features: new WirelessNetworkFeatures(
                CanToggle: true, CanEditSsid: true, CanEditPassword: true,
                CanEditChannel: false, CanEditChannelWidth: false, CanEditTxPower: false, CanEditBroadcast: true),
            CoupledWith: []);
    }

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
