using TpLink.Sdk.Auth;
using TpLink.Sdk.Models;
using TpLink.Sdk.Session;
using TpLink.Sdk.Transport;

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
    private const string LoginAuthPath = "/login?form=auth";
    private const string LoginPath = "/login?form=login";

    private readonly TpLinkTransport _transport;
    private TpLinkSession? _session;

    public TpLinkRouterClient(string host, bool useHttps = true)
    {
        _transport = new TpLinkTransport(host, useHttps);
    }

    public bool IsAuthenticated => _session is not null;

    /// <summary>
    /// Two-call handshake confirmed live: one `form=auth`/`operation=read` call returns
    /// both the RSA key and `seq` in one shot, then `form=login` with the RSA-encrypted
    /// password. No AES envelope, no signing, no `confirm` field on this firmware.
    /// </summary>
    public async Task LoginAsync(string password, CancellationToken ct = default)
    {
        var authResponse = await _transport.PostFormAsync<TpLinkAuthReadResponse>(
            stok: "", path: LoginAuthPath, formBody: "operation=read", ct);

        if (!authResponse.Success || authResponse.Data is null || authResponse.Data.Key.Count != 2)
            throw new TpLinkProtocolException("form=auth did not return a usable RSA key.");

        var key = new RsaPublicKey(authResponse.Data.Key[0], authResponse.Data.Key[1]);
        var encryptedPassword = RsaPasswordEncryptor.Encrypt(password, authResponse.Data.Seq, key);

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
