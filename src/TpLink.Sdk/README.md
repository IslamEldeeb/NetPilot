# TpLink.Sdk

Standalone, protocol-only client for the TP-Link Archer AX53 (and same-firmware-family routers). No dependency on anything NetPilot-specific — independently publishable; someone could `dotnet add package TpLink.Sdk` for an unrelated project.

## Purpose

Implements the router's HTTP protocol against ground truth captured live from a real device — see [`docs/phase1-live-findings.md`](../../docs/phase1-live-findings.md) — rather than the more complex handshake described in general TP-Link ecosystem documentation. The `smart_network` device-read/write path matches that doc exactly: plain-JSON, no AES envelope, no request signing. The login handshake implemented here (`form=keys` → `form=login`) was refined past that doc's snapshot via later static analysis of the router's own JS bundles (see the `RsaPasswordEncryptor` doc comment) — the findings doc's `form=auth` description is now superseded, not contradicted.

## Folders

| Folder | Contents |
|---|---|
| `Auth/` | `RsaPasswordEncryptor`, `RsaPublicKey` — RSA-encrypts the login password |
| `Session/` | `TpLinkSession` — holds the `stok` auth token |
| `Transport/` | `TpLinkTransport` (raw HTTP), `TpLinkProtocolException` |
| `Models/` | Wire-format DTOs (`TpLinkDeviceRecord` and response envelopes) + lenient JSON converters |
| `TpLinkRouterClient.cs` | The public client surface |
| `TpLinkUsageParser.cs` | Parses the raw `trafficUsage` field into a byte count |

## `TpLinkRouterClient` — public surface

| Method | Endpoint | Notes |
|---|---|---|
| `LoginAsync(password)` | `form=keys` (`operation=read`) → `form=login` (`operation=login`) | 2-call handshake: fetch RSA public key, then POST the RSA-encrypted password. Returns a `stok` token used by every subsequent call — plaintext in the response, no decryption step needed. |
| `GetDevicesAsync()` | `admin/smart_network?form=game_accelerator`, `operation=loadDevice` | One call returns every connected device's current state — no per-device polling. |
| `SetSpeedLimitAsync(mac, enable, downloadKbps, uploadKbps)` | `admin/smart_network?form=client_speed_limit`, `operation=write` | **Different `form` than the read path** on the same section — confirmed live, not a guess. MAC must be dash-separated to match the router's own format. Response is a bare `{"success":true}` with no echoed state — callers should re-fetch `GetDevicesAsync` to confirm rather than trust the write response alone. |
| `GetMaxValuesAsync()` | `admin/smart_network?form=client_speed_limit`, `operation=read_max` | Router's global ceiling values (max rules, max Kbps) — for input validation, not per-device data. |

## Protocol details worth knowing

- **Auth model is per-endpoint, not global.** The classic RSA/AES-envelope handshake documented for legacy TP-Link sections (`admin/wireless`, `admin/network`, `admin/dhcps`) does **not** apply to `admin/smart_network` — those requests and responses are plain form/JSON, authorized purely by the `stok` token in the URL plus the session cookie. `TpLinkTransport` only implements this plain-JSON mode; the encrypted-envelope mode is an open item, not guessed at.
- **RSA padding is PKCS#1 v1.5 of the raw UTF-8 password**, confirmed by reading the router's own front-end JS (`jsbn`'s `RSAKey.encrypt()`) directly — not by trial and error. See the doc comment on `RsaPasswordEncryptor` for the full derivation and one flagged, unresolved discrepancy against an earlier live-rejection report; don't change the padding scheme without a fresh live capture.
- **The router allows exactly one active session** — a second login elsewhere kicks the first one out (`TpLinkSession`).
- **HTTP/1.1 is pinned explicitly** (`DefaultVersionPolicy = RequestVersionExact`) — embedded router HTTP servers are frequently 1.1-only under the hood even without hard-rejecting HTTP/2, and this rules out a whole class of body/framing corruption on POSTs.
- **Self-signed HTTPS cert handling is scoped to the configured host** — not a blanket "accept all certificates" setting.
- **Field typing is lenient by design.** `downloadLimit`/`uploadLimit`/`timePeriod` use `LenientIntConverter` and `trafficUsage`/`onlineTime` use `LenientStringConverter` because the router has been observed returning some numeric fields as either JSON numbers or numeric strings.
- **Usage units are not yet fully confirmed live.** `TpLinkUsageParser.TryParseBytes` defaults to "plain integer = bytes" (matching every other numeric field on this endpoint) with a formatted-string (`"1.2 MB"`-style) fallback — this is the single place to fix if a live capture shows a different unit.

## Consumers

- [`NetPilot.Providers.TpLink`](../NetPilot.Providers.TpLink/README.md) — the only project that references this SDK; adapts it to `IRouterProvider`.

## Tests

[`test/TpLink.Sdk.Tests`](../../test/TpLink.Sdk.Tests) replays JSON fixtures captured live against a real AX53 rather than hand-written guesses — meaningful to run without a router on the network.
