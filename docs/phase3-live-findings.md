# NetPilot — Phase 3 Live Findings (Wireless, Confirmed Against Real AX53)

**Date:** July 24, 2026
**Method:** Claude in Chrome, authenticated by user (password entered by user, not by Claude), read-only exploration — network request capture via injected `fetch`/`XHR` instrumentation. Only §1.1 reads (R1–R6) were captured this pass; **no writes (W1–W5) were performed** — the router's live config was not modified. R7 (reboot endpoint) was skipped per user direction, since `RebootAsync` is already confirmed working from the app in production.

This resolves the single gate called out in `phase3-wireless-management-plan.md` §0. Short version: **the evidence was right — `admin/wireless` is plain JSON, same auth model as `admin/smart_network`.**

---

## The gating question — resolved: no envelope

Every `admin/wireless*` request captured this session used the same auth model already confirmed for Speed Limit in `phase1-live-findings.md`: URL-embedded `stok` + `sysauth` cookie only. Request bodies are plain `application/x-www-form-urlencoded`, one `operation` value per form, no RSA/AES envelope, no signing:

| Form | Request body | Read shape |
|---|---|---|
| `wireless?form=smart_connect` | `operation=read` | `{enable}` |
| `wireless?form=ofdma` | `operation=read` | `{enable}` |
| `wireless?form=twt` | `operation=read` | `{enable}` |
| `wireless?form=region` | `operation=read` | country + per-band channel/width capability lists |
| `wireless?form=wireless_2g` | `operation=read_spf` | full 2.4 GHz main-network config |
| `wireless?form=wireless_5g` | `operation=read_spf` | full 5 GHz main-network config |
| `wireless?form=syspara_2g` / `syspara_5g` | `operation=read` | low-level 802.11 params (frag, wmm, beacon interval, rts, isolate…) — not needed for `WirelessNetworkUpdate` |
| `wireless?form=iot_2g&form=iot_5g&form=iot_5g_2` | `operation=read_spf` | all three IoT sub-bands, one flat prefixed object |
| `wireless?form=guest_2g&form=guest_5g&form=guest_2g5g` | `operation=read` | both guest bands + shared `guest_2g5g_*` fields, one flat prefixed object |
| `wireless?form=guestnetwork_bandwidth_ctrl` | `operation=read` | guest network's own speed cap (`-1` = unlimited, same convention as Speed Limit) |
| `wireless_schedule_v2?form=settings` | `operation=inforce` | `{"inforce": false}` — router has its own WiFi schedule engine, **currently off** |
| `firmware?form=upgrade` | `operation=read` | model + firmware version (fixes `GetRouterInfoAsync`) |

Responses are all plain, readable JSON — same as `admin/smart_network`. **`TpLinkTransport`'s "never live-verified" comment on `admin/wireless` can be removed; no encrypted-envelope mode is needed for any endpoint captured so far.**

## Critical finding: PSK reads back in cleartext, unmasked

Every wireless form's response includes the real WiFi password verbatim under a `*psk_key` field (`psk_key` for main networks, `iot_2g_psk_key`/`iot_5g_psk_key`, `guest_2g5g_psk_key`, etc.) — **not** masked as `********`.

This directly answers phase3 §1.3.3: **NetPilot can safely round-trip the PSK on a read-modify-write without ever persisting it.** `NetPilot.Data` §5's "never persist a WiFi PSK" plan holds — read the current value immediately before any write, merge, write, discard. No password-re-supply requirement on the API for edits that don't change the password itself.

## Form → network mapping (resolves R1–R3)

| `WirelessNetworkId` | Form(s) | SSID field | Enable field (in the form's own read) | Notes |
|---|---|---|---|---|
| `main-2.4g` | `wireless_2g` | `ssid` | `enable` (`"on"`/`"off"`) | Smart Connect was **on** at capture time — `main-2.4g` and `main-5g` shared one SSID (`ssid` value identical across both forms, same PSK) |
| `main-5g` | `wireless_5g` | `ssid` | `enable` | Same as above |
| `iot-2.4g` | `iot_2g` (of the combined `iot_2g&iot_5g&iot_5g_2` call) | `iot_2g_ssid` | `iot_2g_enable` | Confirmed on at capture time |
| `iot-5g` | `iot_5g` | `iot_5g_ssid` | `iot_5g_enable` | Off at capture time. **A third band exists on the wire, `iot_5g_2`, currently empty/disabled** — likely a mesh/secondary-radio slot, not a fourth `WirelessNetworkId` NetPilot needs to model unless it's ever populated |
| `guest-2.4g` | `guest_2g` (of the combined `guest_2g&guest_5g&guest_2g5g` call) | `guest_2g_ssid` | `guest_2g_enable` | Off at capture time |
| `guest-5g` | `guest_5g` | `guest_5g_ssid` | `guest_5g_enable` | Off at capture time. Guest also has shared `guest_2g5g_*` fields (password, encryption, redirect) mirroring the `main-*`/Smart Connect pattern — a password change likely has to go through the shared key, not per-band, when Smart Connect-equivalent guest linking is active |

**IoT form-name unknown from phase3 §1.1 R2 is resolved: `iot_2g` / `iot_5g` / `iot_5g_2`, read together in a single POST with three `form` params, returning one flat object with prefixed keys** — not the `_2.4G`-suffixed variant guessed in the plan.

## Field shape (per main/IoT/guest form)

Representative fields from `wireless_2g` (main-2.4g), password redacted:

```json
{
  "enable": "on",
  "disabled": "off",
  "ssid": "Wolf",
  "hidden": "off",
  "psk_key": "<redacted — confirmed cleartext, unmasked>",
  "encryption": "4",
  "wpa_version": "auto",
  "psk_version": "sae_transition",
  "psk_cipher": "aes",
  "channel": "6",
  "current_channel": "6",
  "txpower": "high",
  "hwmode": "bgnax",
  "htmode": "auto",
  "macaddr": "AC-A7-F1-02-9D-4B"
}
```

`wireless_5g` is the same shape; observed `channel: "auto"`, `current_channel: "36"`, `htmode: "160"` (channel width in MHz as a string — `"auto"`/`"160"`/`"80"`/`"40"`/`"20"` per the `region` form's capability lists, not yet enumerated exhaustively).

Mapping to `WirelessNetworkState`/`WirelessNetworkUpdate` (`Wireless.cs`):

| Abstraction field | Wire field | Notes |
|---|---|---|
| `Enabled` | `enable` (`"on"`/`"off"`) | **Not** `1`/`0`/`true` — confirms phase3 §2's `bool` mapping needs explicit `"on"`/`"off"` string conversion in the SDK DTO, same convention as `enableLimit` in Speed Limit |
| `Ssid` | `ssid` | |
| `Password` | `psk_key` | Cleartext both directions per the finding above |
| `SsidBroadcast` | `hidden`, **inverted** (`hidden: "off"` → broadcasting) | |
| `Channel` | `channel` (configured) vs `current_channel` (live/actual) — provider should read `channel` for round-tripping a write, expose `current_channel` (or just `channel` when it's not `"auto"`) as the observed value | |
| `ChannelWidth` | `htmode` | |
| `TxPower` | `txpower` (`"high"` observed; likely also `"medium"`/`"low"` per typical TP-Link convention, not directly observed this pass) | |

Two-tier read (`channel` = desired/configured, `current_channel` = actual) means `WirelessNetworkState` may want to distinguish "what's configured" from "what's currently active" for `channel: "auto"` cases — worth a small doc note when `Wireless.cs` is revisited, not necessarily a new field yet.

## Cheap single-call read confirmed (resolves R4)

`admin/status?form=all` (`operation` unspecified/default) returns **every** radio's enable/disable state, plus SSID/channel/PSK, across `wireless_2g_*`, `wireless_5g_*`, `iot_2g_*`, `iot_5g_*`, `guest_2g_*`, `guest_5g_*`, and mesh-related `mlo_host_*` prefixes, in one response (203 top-level keys observed). Confirmed present:

```
wireless_2g_enable, wireless_5g_enable, iot_2g_enable, iot_5g_enable,
guest_2g_enable, guest_5g_enable  — all "on"/"off"
```

**This is a real option for the wireless sweep (phase3 §6.2)**: one `status?form=all` call per ~30 s tick can populate observed state for every network at once, instead of 6 separate form reads. Whether it's *sufficient* on its own (vs. needing the per-form reads for `txpower`/`htmode`/`hidden` — not all confirmed present in this payload within the slice inspected) is worth confirming with a full-field diff before committing the provider implementation to it exclusively; treat it as the sweep's fast path and the per-form reads as the source of truth for full `WirelessNetworkState` on demand.

## Schedule engine (resolves R5)

`admin/wireless_schedule_v2?form=settings` (`operation=inforce`) → `{"success":true,"data":{"inforce":false}}`. The router has its own WiFi scheduling capability and it is **currently disabled**. Directly informs phase3 §10 open question 1: since it's off today, NetPilot can own scheduling cleanly without an existing router-side schedule to conflict with or migrate away from — no evidence the user has ever configured one.

## Writes — confirmed live for the guest network (partial W1/W2, guest scope only)

**Date of this addendum:** July 24, 2026, same-day follow-up session. **Scope: guest network only** (2.4 GHz, zero clients, toggled off→on→off, plus a standalone SSID correction). Main and IoT networks were **not** write-tested — different structural shape (separate per-band forms vs. guest/IoT's combined forms) and higher blast radius (real connected devices), so nothing below should be assumed to generalize to them without their own capture.

**Write endpoint reuses the read form, `operation=write`, plain form-urlencoded body:**

```
POST admin/wireless?form=guest_2g&form=guest_5g&form=guest_2g5g
Content-Type: application/x-www-form-urlencoded   ← required; a bare fetch() defaulting to
                                                       text/plain returned success:true but
                                                       silently applied nothing
Body: operation=write&guest_2g_ssid=TP-Link_Guest_9D4C
→ {"success":true,"data":{ ...full current guest_2g/guest_5g/guest_2g5g object, same shape as
     the read response... }}
```

**Confirmed: writes are a true partial delta, not a whole-object PUT.** A body containing only `operation=write&guest_2g_ssid=<value>` — nothing else, no `enable`, no `psk_key`, no `encryption` — changed the SSID and left every other field (including `guest_2g_enable`) exactly as it was. This closes phase3 §1.3 question 2 for the guest network: **no read-modify-write is needed; the provider can send just the changed fields.** (The dashboard's own UI is *not* a reliable model of this — it resent the `guest_2g5g_encryption`/`psk_version`/`psk_key` triplet on every save regardless of what was actually edited, and dropped `ssid`/`hidden` entirely once its form panel collapsed. That's client-side form-state behavior, not a router requirement — confirmed by the standalone `ssid`-only write above working perfectly with none of those extra fields present.)

**Response echoes full state.** The write response is the same shape as a read (not a bare `{"success":true}` like Speed Limit's write) — `ApplyWirelessAsync` can use it directly as the returned `WirelessNetworkState` without a separate re-read call.

**Field routing confirmed for guest:** `enable`/`ssid`/`hidden` are per-band (`guest_2g_*` / `guest_5g_*`); password/security (`psk_key`/`psk_version`/`encryption`) live under the shared `guest_2g5g_*` prefix, not per-band — confirmed because a plain enable-toggle write still carried the `guest_2g5g_*` triplet in the UI's own save, consistent with the read side's shared-block structure. **A guest password write should therefore target `guest_2g5g_psk_key`, not `guest_2g_psk_key`/`guest_5g_psk_key`** (those per-band PSK fields exist on read but their write behavior is untested).

**Not tested at all, even for guest:** channel/width/TX power writes (guest doesn't have these fields — confirmed absent on read, consistent with §"Form → network mapping" above). Main-band writes and whether toggling main-2.4g couples to iot-2.4g (W4) remain fully open.

## Writes — confirmed live for IoT (2.4 GHz), via user-captured DevTools traffic

**Source:** the user made a real change to the IoT 2.4 GHz network through the dashboard's own UI and pasted the resulting request from their browser's DevTools — not something this session generated. This is the most authoritative capture of the three (guest, this one, and none yet for main).

```
POST admin/wireless?form=iot_2g&form=iot_5g          ← note: no iot_5g_2, unlike the 3-form read
Content-Type: application/x-www-form-urlencoded
Body: iot_2g_enable=on&iot_2g_ssid=Wolf-IOT&iot_2g_encryption=1&iot_2g_psk_key=<redacted>
      &iot_2g_hidden=off&iot_5g_enable=off&operation=write_spf
```

**`operation=write_spf`, not `operation=write`.** This is the key finding — it diverges from guest. The pattern is now clear: the write operation mirrors that network kind's *read* operation suffix. Guest reads via `operation=read` → writes via `operation=write`. Main/IoT read via `operation=read_spf` → write via `operation=write_spf`. (This retroactively validates a hypothesis the Opus research pass flagged as low-confidence — "watch for it" — which turned out correct for this family, wrong to dismiss.)

**Field routing differs from guest: IoT keeps its password per-band.** `iot_2g_psk_key`, not a shared `iot_2g5g_*` block — guest's shared-password structure does **not** generalize to IoT. This matches the read-side shape (no `iot_2g5g_*` fields exist on read either) but is worth stating explicitly since it was an open question.

**Not independently confirmed from this one capture:** whether `iot_2g_encryption` is *required* alongside `iot_2g_psk_key` for a password change, or just happened to be resent because the UI had it loaded (same caveat as guest's UI always resending its security triplet). Until a password-only write is isolated and tested (the way guest's SSID-only write was), **treat IoT password writes as unconfirmed** — enable/SSID/hidden are solid, password is not yet.

## Writes — Main network, captured live, and it caused a real (self-resolved) outage

**Source:** user-captured DevTools traffic, same method as the IoT capture. The user unchecked the top-level "2.4 GHz / 5 GHz: Enabled" master checkbox on the Wireless Settings page (not a per-band toggle) and hit Save. **This actually disabled both main radios on the live router** — main WiFi (and everything riding those radios) went down until the user manually re-enabled it through the router UI. Household WiFi was down for several minutes as a direct result of this capture. Confirmed back up before continuing.

```
POST admin/wireless?form=wireless_2g&form=wireless_5g
Body: operation=write_spf
      &wireless_2g_enable=off&wireless_2g_hwmode=bgnax&wireless_2g_txpower=high&wireless_2g_mu_mimo=off
      &wireless_2g_ssid=Wolf&wireless_2g_hidden=off&wireless_2g_encryption=4&wireless_2g_psk_key=<redacted>
      &wireless_2g_channel=6&wireless_2g_htmode=auto&wireless_2g_disabled_all=on
      &wireless_5g_enable=off&wireless_5g_txpower=high&wireless_5g_mu_mimo=off
      &wireless_5g_ssid=Wolf&wireless_5g_hidden=off&wireless_5g_encryption=4&wireless_5g_psk_key=<redacted>
      &wireless_5g_disabled_all=on&wireless_5g_channel=auto&wireless_5g_htmode=160
```

**`operation=write_spf` confirmed for main too** — consistent with the now-solid rule (read `_spf` suffix ⇒ write `_spf` suffix). Form combines both bands in one write (`form=wireless_2g&form=wireless_5g`), same shape as IoT's write form.

**This is a WHOLE-OBJECT write, not a partial delta — main does NOT follow guest/IoT's pattern.** The body carries essentially everything for both bands (`enable`, `hwmode`, `txpower`, `mu_mimo`, `ssid`, `hidden`, `encryption`, `psk_key`, `channel`, `htmode`, `disabled_all`) even though the user's only actual intent was toggling one master switch. This is the clearest evidence yet that Smart Connect's 2.4G/5G pairing makes the UI treat "main wireless" as one unit that gets fully resent on any change — the opposite of guest's proven single-field partial write. **Do not assume a minimal `wireless_2g_enable=off` alone would work** — that's genuinely untested, and given what a full-object write just did to household connectivity, isolating it is not a low-risk test to run casually.

**A second, previously-unconfirmed field surfaced: `disabled_all`.** Present as `wireless_2g_disabled_all` / `wireless_5g_disabled_all`, set to `"on"` alongside `enable=off` — likely the field behind the "2.4 GHz / 5 GHz: Enabled" master checkbox specifically (distinct from Smart Connect and from any future per-band-only enable). Not present in any prior read capture's field list transcribed in this doc — worth adding to `TpLinkMainBandConfig` if main writes are ever implemented.

**Recommendation: do not wire main-network writes into `ApplyWirelessAsync` from this capture alone.** One data point, whole-object shape, unconfirmed whether a smaller body is safe, and the one live test already caused a real (if brief) outage. Main is the network every device depends on; a write bug here has no "zero clients" safety net the way guest and this IoT test did. If main write support is wanted, it needs a deliberate follow-up session — ideally testing a minimal body in isolation first, at a time when a mistake is tolerable.

## Firmware/model endpoint (resolves R6)

```
POST admin/firmware?form=upgrade
Body: operation=read
→ {"success":true,"data":{
     "model":"Archer AX53",
     "hardware_version":"Archer AX53 v1.0",
     "firmware_version":"1.7.1 Build 20260213 rel.87654(4547)",
     "upgraded":false,"upgradetime":16,"totaltime":128,"is_default":false}}
```

Matches `phase1-live-findings.md`'s router identity exactly (same firmware string). `GetRouterInfoAsync`'s `"unknown (unverified endpoint)"` placeholder can be replaced with a real call to this form.

## What this changes in the phase3 plan

- §0's gate is closed: `TpLinkTransport` does **not** need an encrypted-envelope mode for wireless. The plain-JSON transport already used for `smart_network` covers it.
- §3 (`TpLink.Sdk` wireless DTOs): field names above are ready to encode directly into `TpLinkWirelessConfig`/`TpLinkWirelessForm` constants. `LenientStringConverter`/`LenientIntConverter` will still be needed — `channel` was seen as both a numeric string (`"6"`) and the literal `"auto"` on the same field across bands, so it can't be a plain `int`.
- §5 (`NetPilot.Data`): "never persist a WiFi PSK" is confirmed safe and sufficient — no masked-PSK fallback path is needed.
- §1.3 questions 1 (envelope), 3 (PSK masking), 5 partially (session survived the capture without re-login, `stok` stayed valid across all reads) are answered. Question 2 (whole-object vs. partial write) is now answered **for guest** — partial, no read-modify-write needed. Question 4 (shared-radio coupling) and 6 (does a wireless write disturb Speed Limit config) remain open. Main/IoT write shape is a new open item (see below) — the guest confirmation narrows it but doesn't close it.

## Remaining open items

1. **Main-band writes: shape known, safety not.** Now captured (see "Writes — Main network" above) — `admin/wireless?form=wireless_2g&form=wireless_5g`, `operation=write_spf`, whole-object body. But it's one data point from a master-checkbox toggle that took the household's WiFi down, so whether a smaller/safer body works is unknown, and it hasn't been wired into `ApplyWirelessAsync`. Channel/width/TX-power writes still have zero evidence from any source. Shared-radio coupling (does toggling `main-2.4g` off also kill `iot-2.4g`?) and session survival across a radio-affecting write remain unconfirmed — this capture didn't isolate those either. Any further main-network testing needs its own explicit go-ahead and should not be done casually given the demonstrated blast radius.
2. **`txpower` value enumeration** — only `"high"` was observed live; `"medium"`/`"low"` (or whatever the actual enum is) not directly confirmed.
3. **`channelWidth` (`htmode`) valid values** — `"auto"` and `"160"` observed; the `region` form's `capability` object carries full per-band lists (`channelList_80`, `channelList_160`, `htmode_2g`, etc.) but wasn't fully transcribed this pass — worth pulling in full before building channel/width validation.
4. **`status?form=all` field completeness** — confirmed it carries enable/SSID/channel/PSK; not confirmed whether it also carries `txpower`/`htmode`/`hidden` for every network, which determines whether the wireless sweep can rely on it exclusively or must fall back to per-form reads for full state.
5. R7 (reboot action endpoint) intentionally not investigated this pass — reboot already works from the app in production, per user; not a Phase 3 blocker.
