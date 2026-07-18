# NetPilot — Phase 1 Live Findings (Confirmed Against Real AX53)

**Date:** July 15, 2026
**Method:** Claude in Chrome, authenticated by user (password entered by user, not by Claude), read-only exploration — network request capture via injected `fetch`/`XHR` instrumentation + JS bundle inspection. No settings were changed on the live router.

This supersedes the "unconfirmed, best guess" framing in `NetPilot_Research_Findings_and_Architecture.md` §6a/§9 Phase 1. Short version: **the hypothesis was right, and reality is better than hypothesized.**

---

## Login handshake — confirmed live, and it's simpler than §3 described

Captured by logging out and back in with request instrumentation active (user typed the password themselves; Claude only observed the resulting traffic). **This firmware's actual login flow matches neither of the two variants documented in §3 / the OSS library** — it's a third, simpler one:

1. `POST /cgi-bin/luci/;stok=/login?form=auth` — body `operation=read` (no auth needed) →
   `{"success":true,"data":{"key":["<N hex>","<E hex>"],"seq":<number>}}`
   **One call returns both the RSA public key and `seq`** — there is no separate `form=keys` request at all (§3.1 step 1 doesn't happen on this firmware).
2. RSA-encrypt the password against that key (`~2048-bit`, inferred from ciphertext length — single-block encryption, no chunking needed for a normal-length password).
3. `POST /cgi-bin/luci/;stok=/login?form=login` — body is **just** `operation=login&password=<rsa_encrypted_hex>` — confirmed no `sign` field, no `confirm=true`, no AES key/IV exchange, nothing else. Total body was 537 characters, ~511 of which are the encrypted password hex.
4. Response: `{"success":true,"data":{"stok":"<32-char token>"}}` — **plaintext, not AES-wrapped.** The `stok` is directly usable, no decryption step.
5. A `sysauth` cookie is set (standard `Set-Cookie`, not readable from page JS, but confirmed working — all subsequent authenticated calls succeeded using it automatically via the browser's cookie jar).

**Net effect: for this firmware, the entire §3 AES-envelope / RSA-signature machinery (steps 4–8 of §3.1) is unnecessary.** Login is RSA-encrypt-password-and-post, nothing more. This is closer in spirit to the OSS library's `TplinkRouterV1_11` class than the full `TplinkRouter` class, but not identical to either (`TplinkRouterV1_11` still calls a separate `form=keys` endpoint and has no `seq`/`form=auth` step at all). **`TpLink.Sdk`'s login implementation should target this confirmed 2-call flow directly** rather than porting either OSS variant as-is; both should probably still exist as fallbacks for other firmware/models, auto-detected the same way the OSS library does.

**Bonus finding:** immediately after login, the Web UI itself fires a batch of `cloud_account:*` and `privacy_policy:fing_auth_state` calls — TP-Link's own client checks in with TP-Link Cloud (update reminders, device info sync) and with **Fing** (a third-party device-fingerprinting service) right after authenticating. This is a plausible explanation for how `deviceType` gets populated with accurate, human-readable categories — likely backed by Fing's device-identification database rather than a purely local heuristic. Not required for NetPilot to replicate; just useful context for why the categorization is unusually good.

## Router identity (confirmed)

- **Model:** Archer AX53 v1.0
- **Firmware:** `1.7.1 Build 20260213 rel.87654(4547)`
- This is newer than the firmware (~1.5.x) referenced in the Sept–Oct 2025 forum threads that originally reported "no Speed Limit on AX53" — consistent with the feature having been added in a later firmware update, per TP-Link's own moderator note that model coverage expands over time.

## Speed Limit — fully confirmed, endpoint and fields captured live

**UI path:** Network Map → Clients → pencil icon ("Modify" column) → Edit modal (Device Name, Speed Limit "Enabled" checkbox, Download Speed Limit, Upload Speed Limit).

**Actual endpoint (differs from the doc's §6a guess of `admin/dhcps?form=...`):**

```
POST /cgi-bin/luci/;stok=<token>/admin/smart_network?form=game_accelerator
Body: operation=loadDevice   (or operation=loadSpeed for the lighter polling variant)
```

**This endpoint is plaintext JSON — no RSA/AES envelope.** Response shape:

```json
{
  "success": true,
  "data": [
    {
      "index": 0,
      "key": "...",
      "mac": "...",
      "ip": "...",
      "host": "...",
      "deviceName": "C110",
      "deviceType": "IP Camera",
      "deviceTag": "iot_2.4G",
      "isGuest": false,
      "enableLimit": "on",
      "downloadLimit": "5120",
      "uploadLimit": "2048",
      "enablePriority": false,
      "speedLimitOnline": true,
      "timePeriod": -1,
      "remainTime": -1,
      "uploadSpeed": 0,
      "downloadSpeed": 0,
      "txrate": null,
      "rxrate": null,
      "onlineTime": "...",
      "trafficUsage": "...",
      "signal": null
    }
    // ...26 devices total on this network at capture time
  ]
}
```

Field mapping against the doc's §6a hypothesis (all confirmed correct in concept, exact names differ — camelCase, not snake_case):

| Doc's guess (§6a) | Actual live field | Notes |
|---|---|---|
| `dev_type` | **`deviceType`** | Human-readable string, not an opaque int — see category list below |
| `sl_enable` | **`enableLimit`** | String `"on"`/`"off"`, not boolean |
| `down_limit` | **`downloadLimit`** | Kbps, as string (sometimes number — inconsistent typing observed, SDK should parse leniently). `5120` = 5 Mbps, confirmed matches UI display exactly |
| `up_limit` | **`uploadLimit`** | Kbps, same typing note. `2048` = 2 Mbps, confirmed |
| `qos_prior` | **`enablePriority`** | Boolean. Present and readable on every device, not just via Tether — see below |
| `pri_time` | **`timePeriod`** | `-1` = no schedule / always-on when limit is enabled |
| (new) | `deviceTag` | Connection info, not category: observed values `5G`, `2.4G`, `wired`, `iot_2.4G`, `offline` |
| (new) | `speedLimitOnline` | Boolean, purpose not yet fully clear — possibly "is this device currently subject to its limit" |

**Units confirmed:** Kbps. `-1` appears to mean "unlimited" for a limit field. Verified against UI: `downloadLimit: "5120"` rendered as "5 Mbps" and `uploadLimit: "2048"` rendered as "2 Mbps" for device C110.

### Device category enum — confirmed, and it already matches the business requirement

`deviceType` observed values across the 26 connected devices on this network:

```
Laptop, Game Console, Computer, IP Camera, Desktop, Mobile, Smart Device,
Tablet, Media Player, AV Receiver, Smart Meter, IoT Devices, Television
```

**This directly covers the categories in the business goal** — `Mobile` → Phones, `Television` → TVs, `IP Camera` → Cameras, `Game Console` → Game Consoles — assigned by the router's own firmware, no custom classifier required to get started. NetPilot's `DeviceClassifier` (doc §8) can likely be reduced to: *read `deviceType` from the router first; fall back to MAC-OUI/hostname heuristics only for devices the router reports as generic (`Smart Device`, `IoT Devices`) or fails to classify.* This is a meaningfully simpler starting point than originally planned.

### Global config endpoint (separate, also plaintext)

```
POST .../admin/smart_network?form=client_speed_limit
Body: operation=read_max
→ {"success":true,"data":{"max_rules": <n>, "downloadLimitMax": <n>, "uploadLimitMax": <n>}}
```

This returns the router's global ceiling values (max number of speed-limit rules, max Kbps settable) — useful for input validation in the SDK, not per-device data.

## Auth model correction — not every endpoint uses the RSA/AES envelope

The doc's §3 handshake (RSA password encryption → session AES key → signed requests) is confirmed correct for login and for the legacy `admin/wireless`, `admin/network`, `admin/dhcps` style endpoints (per the OSS library source). **But `admin/smart_network` (Speed Limit, Game Accelerator) does not use it at all** — requests are plain `operation=loadDevice`-style bodies, responses are plain JSON, and authorization is enforced purely by the `stok` token in the URL plus the `sysauth` cookie, no per-request signing/encryption.

**Implication for `TpLink.Sdk`:** the transport layer needs two request modes, not one — an encrypted-envelope mode (legacy sections) and a plain-JSON mode (smart_network and possibly other newer sections). This should be modeled as a per-endpoint concern (e.g., a flag on each `TpLinkRequest`), not a global client setting.

## QoS Priority / HomeShield — confirmed Tether/subscription-gated, exactly as originally researched

- Navigated live to **Advanced → HomeShield → Network Analysis & Optimization**: TP-Link explicitly lists **"Quality of Service — Prioritize your devices so that the most frequently used and data-demanding ones get the bandwidth they need"** as a feature here — but the entire page is a marketing surface ("Download Tether to enjoy the HomeShield service... Get a 3-month free trial. Subscribe now") with no actionable controls. This matches the original forum-sourced finding exactly.
- **However:** the `enablePriority` field is present and readable (confirmed `false`/`true` per device) in the same plain-JSON `game_accelerator` payload used for Speed Limit — meaning the underlying data model already carries this flag outside of any Tether-specific channel. Whether it's writable from this same endpoint is not yet confirmed (see Open Items).
- **No dedicated "QoS" or "Bandwidth Control" menu exists under Advanced → NAT Forwarding** on this firmware (only Port Forwarding, Port Triggering, UPnP, DMZ) — the generic older TP-Link support articles describing that menu path don't apply to this unit.
- JS bundle inspection found `qos` and `qosAdv` i18n key groups (`devicePriority`, `highPriority`, `downloadBandwidth`, `uploadBandwidth`, `schedule`, `repeat`, `nextDayTip`, etc.) confirming a full Priority-scheduling feature exists in the compiled front-end code, gated by the same subscription/app wall.

## Write path — confirmed live (with user's explicit go-ahead)

User approved testing an actual write against a real device (`Galaxy-S9`, MAC ending `...B3-D1`, phone on 2.4G). To keep it zero-risk, the test **resubmitted the device's existing values unchanged** (1 Mbps down / 200 Kbps up / enabled) rather than picking new ones — this exercises the real write path end-to-end while leaving the device's actual configuration identical to before the test. Confirmed via the Network Map UI afterward: still shows 200 Kbps↑ / 1 Mbps↓, unchanged.

**Write endpoint (note: different `form` than the read path):**

```
POST /cgi-bin/luci/;stok=<token>/admin/smart_network?form=client_speed_limit
Body: operation=write&mac=<XX-XX-XX-XX-XX-XX>&enableLimit=on&downloadLimit=1024&uploadLimit=200
→ {"success": true}
```

Confirmed details:
- **Read and write use different `form` values on the same `smart_network` section:** reads (device list) go through `form=game_accelerator` (`operation=loadDevice`/`loadSpeed`); writes (and the global max-values read) go through `form=client_speed_limit` (`operation=write` / `operation=read_max`). A client implementation needs to know both, not just one.
- MAC address format in the write body uses dashes (`XX-XX-XX-XX-XX-XX`), matching the format returned by the read endpoint's `mac` field — no reformatting needed between read and write.
- `downloadLimit`/`uploadLimit` in the write body are plain integers in Kbps (matches read-side units: `1024` = 1 Mbps, `200` = 200 Kbps).
- `enableLimit` is the literal string `on` (or presumably `off` to disable) — matches the read-side field name and value convention exactly.
- The write request is **plain, unencrypted `operation=write&...` form body** — same auth model as every other `smart_network` call (session cookie + stok only, no RSA/AES envelope). Confirms the §3 encrypted-envelope requirement does not apply to this whole section.
- Response is a minimal `{"success": true}` with no echoed data — the SDK should re-fetch (`loadDevice`) to confirm applied state rather than trusting the write response body.

This closes out the **primary open item** from the first pass of this document. `TpLinkSpeedLimitEnforcer` (doc §7) now has a fully specified read *and* write contract, live-verified, ready to implement in Phase 2.

## Remaining open items

1. Whether `enablePriority` is writable via this same `client_speed_limit` write endpoint (e.g., as an extra field alongside `enableLimit`), or requires the Tether/TMP channel exclusively — not tested. Reasonable next experiment: attempt `operation=write` with an added `enablePriority=true` field in Phase 2 and see if it's accepted or ignored.
2. Exact meaning of `speedLimitOnline` — plausible guess ("currently being enforced" vs. "configured but device offline") not verified against behavior.
3. TMP protocol (port ~20002, doc §6b) — out of scope for this pass per user's earlier direction; not tested against this specific router.

## What this changes in the main doc / architecture

- §6a's field-name guesses (`sl_enable` etc.) should be replaced with the confirmed camelCase names above.
- §9 Phase 1 items (a)–(d) are now done; item (e) (TMP port check) was skipped per user's stated scope decision.
- The primary enforcement path (`TpLinkSpeedLimitEnforcer`, doc §7) now targets `admin/smart_network?form=game_accelerator`, not `admin/dhcps`.
- `DeviceClassifier` (doc §8) can lean on the router's own `deviceType` first, custom heuristics second — simplification, not originally planned this way.
- The traffic-shaping bridge fallback (doc §5 option 2) is very likely unnecessary now — Speed Limit is real, live, and its read path is fully mapped.
