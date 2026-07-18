using System.Net;
using System.Text.Json;

namespace TpLink.Sdk.Transport;

/// <summary>
/// Low-level HTTP transport for the confirmed-live plain-JSON request family
/// (`admin/smart_network` — read via form=game_accelerator, write via
/// form=client_speed_limit — plus the login handshake, which is also plain JSON on this
/// firmware per phase1-live-findings.md). No AES/RSA-signing envelope on this path.
///
/// A second, encrypted-envelope mode exists in the wider TP-Link ecosystem for legacy
/// sections (admin/wireless, admin/network, admin/dhcps, admin/firmware) per
/// NetPilot_Research_Findings_and_Architecture.md §3.1 — that mode is NOT implemented here
/// because it was never live-verified against this specific firmware (only the Speed Limit
/// path was, per the phased plan). Callers needing a legacy section should treat it as an
/// open item requiring a live capture before implementing, not a guess.
/// </summary>
public sealed class TpLinkTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;

    public TpLinkTransport(string host, bool useHttps)
    {
        _baseUri = new Uri($"{(useHttps ? "https" : "http")}://{host}");

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };

        if (useHttps)
        {
            // Scoped specifically to the configured router host — not a blanket accept-all-certs
            // setting. The AX53's HTTPS cert is self-signed (confirmed live).
            handler.ServerCertificateCustomValidationCallback = (request, _, _, _) =>
                request.RequestUri is not null && request.RequestUri.Host.Equals(host, StringComparison.OrdinalIgnoreCase);
        }

        _http = new HttpClient(handler) { BaseAddress = _baseUri };
    }

    /// <summary>POSTs a form body to `/cgi-bin/luci/;stok=&lt;stok&gt;/{path}` and parses the JSON response.</summary>
    public async Task<T> PostFormAsync<T>(string stok, string path, string formBody, CancellationToken ct)
    {
        var url = $"/cgi-bin/luci/;stok={stok}{path}";
        using var content = new StringContent(formBody);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<T>(json)
            ?? throw new TpLinkProtocolException($"Empty/unparseable response from {path}: {json}");
        return result;
    }

    public void Dispose() => _http.Dispose();
}
