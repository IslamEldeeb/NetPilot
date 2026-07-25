using TpLink.Sdk.Auth;
using TpLink.Sdk.Models;
using TpLink.Sdk.Session;
using TpLink.Sdk.Transport;
using TpLink.Sdk.Wireless;

namespace TpLink.Sdk;

/// <summary>
/// Protocol-only client for the TP-Link Archer AX53 (and same-firmware-family routers),
/// standalone-publishable — no dependency on anything NetPilot-specific. Targets the
/// confirmed-live 2-call login + plain-JSON smart_network read/write path from
/// docs/phase1-live-findings.md.
/// </summary>
public sealed class TpLinkRouterClient : IDisposable
{
    private const string GameAcceleratorForm = "admin/smart_network?form=game_accelerator";
    private const string ClientSpeedLimitForm = "admin/smart_network?form=client_speed_limit";
    private const string SystemRebootForm = "admin/system?form=reboot";
    private const string FirmwareUpgradeForm = "admin/firmware?form=upgrade";
    private const string LoginKeysPath = "/login?form=keys";
    private const string LoginPath = "/login?form=login";

    private readonly TpLinkTransport _transport;
    private TpLinkSession? _session;

    public TpLinkRouterClient(string host, bool useHttps = true)
    {
        _transport = new TpLinkTransport(host, useHttps);
    }

    public bool IsAuthenticated => _session is not null;

    /// <summary>
    /// Two-call handshake used by the working Archer client: `form=keys`/`operation=read`
    /// returns the RSA key in `data.password`, then `form=login` receives only the
    /// RSA-encrypted password. No AES envelope, signing, or `confirm` field is used.
    /// </summary>
    public async Task LoginAsync(string password, CancellationToken ct = default)
    {
        var keyResponse = await _transport.PostFormAsync<TpLinkPasswordKeyResponse>(
            stok: "", path: LoginKeysPath, formBody: "operation=read", ct);

        if (!keyResponse.Success || keyResponse.Data is null || keyResponse.Data.Password.Count != 2)
            throw new TpLinkProtocolException("form=keys did not return a usable RSA key.");

        var key = new RsaPublicKey(keyResponse.Data.Password[0], keyResponse.Data.Password[1]);
        var encryptedPassword = RsaPasswordEncryptor.Encrypt(password, key);

        var loginResponse = await _transport.PostFormAsync<TpLinkLoginResponse>(
            stok: "", path: LoginPath, formBody: $"operation=login&password={encryptedPassword}", ct);

        if (!loginResponse.Success || loginResponse.Data is null || string.IsNullOrEmpty(loginResponse.Data.Stok))
            throw new TpLinkProtocolException("Login rejected — check password, or another session may hold the router's single login slot.");

        _session = new TpLinkSession(loginResponse.Data.Stok, DateTimeOffset.UtcNow);
    }

    /// <summary>One HTTP call returns every connected device's current state — no per-device polling.</summary>
    public async Task<IReadOnlyList<TpLinkDeviceRecord>> GetDevicesAsync(CancellationToken ct = default)
    {
        var stok = RequireSession();
        var response = await _transport.PostFormAsync<TpLinkLoadDeviceResponse>(
            stok, $"/{GameAcceleratorForm}", "operation=loadDevice", ct);

        if (!response.Success)
            throw new TpLinkProtocolException("loadDevice returned success:false.");

        return response.Data;
    }

    /// <summary>
    /// Confirmed live: different `form` than the read path, same `smart_network` section.
    /// MAC must be dash-separated (XX-XX-XX-XX-XX-XX) to match the router's own format.
    /// </summary>
    public async Task SetSpeedLimitAsync(string macAddress, bool enable, int? downloadKbps, int? uploadKbps, CancellationToken ct = default)
    {
        var stok = RequireSession();
        var enableValue = enable ? "on" : "off";
        var body = $"operation=write&mac={macAddress}&enableLimit={enableValue}" +
                   $"&downloadLimit={downloadKbps ?? 0}&uploadLimit={uploadKbps ?? 0}";

        var response = await _transport.PostFormAsync<TpLinkWriteResponse>(stok, $"/{ClientSpeedLimitForm}", body, ct);

        if (!response.Success)
            throw new TpLinkProtocolException($"SetSpeedLimit write rejected for {macAddress}.");

        // Response is a minimal {"success":true} with no echoed data — callers should
        // re-fetch (GetDevicesAsync) to confirm applied state rather than trust this alone,
        // per phase1-live-findings.md.
    }

    /// <summary>
    /// Reboots the router. UNCONFIRMED against this firmware — unlike the smart_network path
    /// (login, GetDevices, SetSpeedLimit), this endpoint was never live-verified via Claude in
    /// Chrome; it comes only from NetPilot_Research_Findings_and_Architecture.md §3.3's
    /// reverse-engineered API table (`admin/system?form=reboot`), which predates and was
    /// superseded in part by phase1-live-findings.md — that doc corrected a different endpoint
    /// guess (Speed Limit) from the same source, so this one should be treated as similarly
    /// unreliable until confirmed live. It's also unknown whether `admin/system` requires the
    /// RSA/AES-signed envelope used by other legacy sections (never implemented in
    /// TpLinkTransport, see its header comment) rather than the plain-JSON mode used here — if
    /// this call fails with a signing/format error rather than a clean success/failure, that's
    /// the likely cause. Live-verify via the user's Cowork session before trusting this in
    /// production; the caller should surface failures clearly rather than silently retrying.
    /// </summary>
    public async Task RebootAsync(CancellationToken ct = default)
    {
        var stok = RequireSession();
        var response = await _transport.PostFormAsync<TpLinkWriteResponse>(stok, $"/{SystemRebootForm}", "operation=write", ct);

        if (!response.Success)
            throw new TpLinkProtocolException("Reboot request rejected by router.");
    }

    /// <summary>
    /// Reads confirmed live per docs/phase3-live-findings.md — plain JSON, same auth model as
    /// GetDevicesAsync/SetSpeedLimitAsync (no RSA/AES envelope needed). Write support for
    /// main/IoT bands is deliberately not implemented here: unlike guest (see
    /// WriteWirelessGuest2GAsync below), main/IoT writes were never captured live, and their
    /// structure differs enough from guest (separate per-band forms, no shared `_2g5g_` block)
    /// that guest's confirmed shape can't be assumed to carry over — needs its own live capture
    /// (phase3-wireless-management-plan.md §1.2) before it can be built safely.
    /// </summary>
    public Task<TpLinkMainBandConfig> GetWireless2GAsync(CancellationToken ct = default) =>
        GetWirelessAsync<TpLinkMainBandConfig>(TpLinkWirelessForm.Wireless2G, TpLinkWirelessForm.Wireless2GReadOperation, ct);

    /// <summary>See GetWireless2GAsync — same confirmed-live read path, 5 GHz band.</summary>
    public Task<TpLinkMainBandConfig> GetWireless5GAsync(CancellationToken ct = default) =>
        GetWirelessAsync<TpLinkMainBandConfig>(TpLinkWirelessForm.Wireless5G, TpLinkWirelessForm.Wireless5GReadOperation, ct);

    /// <summary>
    /// Single call returns both IoT sub-bands (2.4G and 5G) in one flat, prefixed-key object —
    /// confirmed live, not three separate requests despite the three `form` params in the URL.
    /// </summary>
    public Task<TpLinkIotConfig> GetWirelessIotAsync(CancellationToken ct = default) =>
        GetWirelessAsync<TpLinkIotConfig>(TpLinkWirelessForm.Iot, TpLinkWirelessForm.IotReadOperation, ct);

    /// <summary>Same shape as GetWirelessIotAsync but for the two guest bands.</summary>
    public Task<TpLinkGuestConfig> GetWirelessGuestAsync(CancellationToken ct = default) =>
        GetWirelessAsync<TpLinkGuestConfig>(TpLinkWirelessForm.Guest, TpLinkWirelessForm.GuestReadOperation, ct);

    /// <summary>
    /// Confirmed live per docs/phase3-live-findings.md (guest network, same-day write capture).
    /// A true partial delta: only the parameters you pass are sent, everything else on the
    /// router is left untouched — no read-modify-write needed. `password` writes the shared
    /// `guest_2g5g_psk_key` field (confirmed: password/security live in that shared block, not
    /// per-band, even for a 2.4G-only edit). Response echoes the full updated guest config, same
    /// shape as GetWirelessGuestAsync — use it directly rather than re-reading.
    /// </summary>
    public Task<TpLinkGuestConfig> WriteWirelessGuest2GAsync(
        bool? enable = null, string? ssid = null, bool? hidden = null, string? password = null, CancellationToken ct = default) =>
        WriteWirelessGuestAsync("guest_2g", enable, ssid, hidden, password, ct);

    /// <summary>See WriteWirelessGuest2GAsync — same confirmed mechanism, 5 GHz band.</summary>
    public Task<TpLinkGuestConfig> WriteWirelessGuest5GAsync(
        bool? enable = null, string? ssid = null, bool? hidden = null, string? password = null, CancellationToken ct = default) =>
        WriteWirelessGuestAsync("guest_5g", enable, ssid, hidden, password, ct);

    private async Task<TpLinkGuestConfig> WriteWirelessGuestAsync(
        string bandPrefix, bool? enable, string? ssid, bool? hidden, string? password, CancellationToken ct)
    {
        if (enable is null && ssid is null && hidden is null && password is null)
            throw new ArgumentException("At least one field must be provided.");

        var stok = RequireSession();
        var parts = new List<string> { "operation=write" };
        if (enable is not null) parts.Add($"{bandPrefix}_enable={(enable.Value ? "on" : "off")}");
        if (ssid is not null) parts.Add($"{bandPrefix}_ssid={Uri.EscapeDataString(ssid)}");
        if (hidden is not null) parts.Add($"{bandPrefix}_hidden={(hidden.Value ? "on" : "off")}");
        if (password is not null) parts.Add($"guest_2g5g_psk_key={Uri.EscapeDataString(password)}");

        var response = await _transport.PostFormAsync<TpLinkWirelessResponse<TpLinkGuestConfig>>(
            stok, $"/{TpLinkWirelessForm.Guest}", string.Join("&", parts), ct);

        if (!response.Success || response.Data is null)
            throw new TpLinkProtocolException("Guest wireless write returned success:false or no data.");

        return response.Data;
    }

    /// <summary>
    /// Confirmed live per docs/phase3-live-findings.md, from user-captured DevTools traffic
    /// (not generated by this SDK's own testing) — `operation=write_spf`, NOT `operation=write`
    /// like guest. IoT keeps its password per-band (`{band}_psk_key`), unlike guest's shared
    /// block. Passing `password` is supported here for protocol completeness, but whether
    /// `encryption` must travel alongside a password change was never isolated and confirmed —
    /// callers should treat a password-only IoT write as unverified until that capture happens.
    /// </summary>
    public Task<TpLinkIotConfig> WriteWirelessIot2GAsync(
        bool? enable = null, string? ssid = null, bool? hidden = null, string? password = null, CancellationToken ct = default) =>
        WriteWirelessIotAsync("iot_2g", enable, ssid, hidden, password, ct);

    /// <summary>See WriteWirelessIot2GAsync — same confirmed mechanism, 5 GHz band.</summary>
    public Task<TpLinkIotConfig> WriteWirelessIot5GAsync(
        bool? enable = null, string? ssid = null, bool? hidden = null, string? password = null, CancellationToken ct = default) =>
        WriteWirelessIotAsync("iot_5g", enable, ssid, hidden, password, ct);

    private async Task<TpLinkIotConfig> WriteWirelessIotAsync(
        string bandPrefix, bool? enable, string? ssid, bool? hidden, string? password, CancellationToken ct)
    {
        if (enable is null && ssid is null && hidden is null && password is null)
            throw new ArgumentException("At least one field must be provided.");

        var stok = RequireSession();
        var parts = new List<string>();
        if (enable is not null) parts.Add($"{bandPrefix}_enable={(enable.Value ? "on" : "off")}");
        if (ssid is not null) parts.Add($"{bandPrefix}_ssid={Uri.EscapeDataString(ssid)}");
        if (hidden is not null) parts.Add($"{bandPrefix}_hidden={(hidden.Value ? "on" : "off")}");
        if (password is not null) parts.Add($"{bandPrefix}_psk_key={Uri.EscapeDataString(password)}");
        parts.Add(TpLinkWirelessForm.IotWriteOperation);

        var response = await _transport.PostFormAsync<TpLinkWirelessResponse<TpLinkIotConfig>>(
            stok, $"/{TpLinkWirelessForm.IotWrite}", string.Join("&", parts), ct);

        if (!response.Success || response.Data is null)
            throw new TpLinkProtocolException("IoT wireless write returned success:false or no data.");

        return response.Data;
    }

    private async Task<T> GetWirelessAsync<T>(string form, string operation, CancellationToken ct)
    {
        var stok = RequireSession();
        var response = await _transport.PostFormAsync<TpLinkWirelessResponse<T>>(stok, $"/{form}", operation, ct);

        if (!response.Success || response.Data is null)
            throw new TpLinkProtocolException($"{form} returned success:false or no data.");

        return response.Data;
    }

    /// <summary>Model + firmware version — confirmed live per docs/phase3-live-findings.md.</summary>
    public async Task<TpLinkFirmwareInfo?> GetFirmwareInfoAsync(CancellationToken ct = default)
    {
        var stok = RequireSession();
        var response = await _transport.PostFormAsync<TpLinkFirmwareInfoResponse>(stok, $"/{FirmwareUpgradeForm}", "operation=read", ct);
        return response.Success ? response.Data : null;
    }

    /// <summary>Router's global ceiling values — useful for input validation, not per-device data.</summary>
    public async Task<TpLinkMaxValuesData?> GetMaxValuesAsync(CancellationToken ct = default)
    {
        var stok = RequireSession();
        var response = await _transport.PostFormAsync<TpLinkMaxValuesResponse>(
            stok, $"/{ClientSpeedLimitForm}", "operation=read_max", ct);
        return response.Success ? response.Data : null;
    }

    private string RequireSession() =>
        _session?.Stok ?? throw new InvalidOperationException("Not authenticated — call LoginAsync first.");

    public void Dispose() => _transport.Dispose();
}
