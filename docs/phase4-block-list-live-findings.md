# NetPilot — Block List (Access Control) Live Findings

**Date:** July 25, 2026
**Method:** User captured two curl requests directly from the browser DevTools network tab while
using the router's own Access Control / block-device UI (insert one device, then remove one
device). Not an independent live-capture session — these are the only two data points available.

## Endpoint — confirmed live

```
POST /cgi-bin/luci/;stok=<token>/admin/access_control?form=black_list
```

Same auth model as `admin/smart_network` (Speed Limit): `stok` in the URL, `sysauth` cookie,
plain `application/x-www-form-urlencoded` body, no RSA/AES envelope. `TpLinkTransport.PostFormAsync`
is reused as-is.

### Insert (block a device)

```
operation=insert&new=<url-encoded JSON>&index=0
```

Captured `new` blob (decoded):

```json
{"name":"Galaxy-S9","deviceType":"Mobile","mac":"E6-89-81-77-B3-D1","ipaddr":"192.168.1.117","host":"NON_HOST","conn_type":"wireless","key":"kHxVEnTs7fX_LMHMC6EJB"}
```

Response: `{"success": true}` (same minimal shape as Speed Limit's write endpoint — no echoed data).

### Remove (unblock a device)

```
operation=remove&key=<key>&index=0
```

Response: `{"success": true}`.

## What's confirmed vs. inferred

**Confirmed:**
- The endpoint, both `operation` values, and the overall body shape for each.
- Every field in the `new` blob except `conn_type` maps 1:1 onto the existing `TpLinkDeviceRecord`
  shape from `admin/smart_network?form=game_accelerator` (`loadDevice`): `name`↔`deviceName`,
  `deviceType`↔`deviceType`, `mac`↔`mac` (same dash format), `ipaddr`↔`ip`, `host`↔`host`
  (including the `NON_HOST` convention already handled by `TpLinkRouterProvider.HasRealHostname`).
- Critically, `key` in the insert blob is present, and `TpLinkDeviceRecord` already carries its own
  `key`/`index` fields (confirmed live back in `phase1-live-findings.md`'s `loadDevice` fixture,
  just not previously bound in the C# model). This strongly implies the blacklist entry's `key` is
  the device's own key from the device list, not a value freshly minted for the blacklist —
  the router UI is evidently building this entry from a device record it already has, not
  generating a new identifier. **Implementation decision:** `TpLinkRouterClient.BlockDeviceAsync`
  re-fetches `GetDevicesAsync` and reuses that record's `key` rather than generating one.

**Inferred, not independently confirmed — open items:**
1. **`conn_type`** ("wireless" in the one sample) — assumed to be `"wired"` for a wired device
   (mirroring the existing `deviceTag == "wired"` check used elsewhere), but no wired-device
   capture exists to confirm this.
2. **The top-level `index=0`** — both captures used `index=0` (one insert, one remove of what
   appear to be different, unrelated entries). Whether this is a literal constant, a position in
   the blacklist itself, or something else entirely (e.g. a batch-operation index) is unconfirmed.
   Kept as a hardcoded `0` for now, matching both observed samples.
3. **No read/list operation was captured** for `form=black_list`. The router UI must have some way
   to display currently-blocked devices, but that request wasn't captured. Without it:
   - NetPilot cannot reconcile blocked state against the router the way it does for Speed Limit
     (`RouterReportedLimit` / drift detection) — blocking is implemented as a direct dashboard
     action (mirrors `RebootAsync`'s pattern), not a reconciled policy.
   - The `key` used to insert is persisted locally (`Device.BlockListToken`) so Unblock doesn't
     depend on the device still appearing in `loadDevice` post-block (plausible if a block causes
     the router to stop tracking/reporting the device at all — untested).
4. **Global Access Control / blacklist-mode enable state** was not captured. If this firmware has
   a separate on/off toggle for the whole Access Control feature, an `insert` could return
   `success:true` while enforcing nothing on a router where it's off. Not verified either way.

## What this means for TpLink.Sdk / NetPilot.Providers.TpLink

- `TpLinkRouterClient.BlockDeviceAsync(macAddress)` looks up the device's current record (for
  `key`, `deviceName`, `deviceType`, `ip`, `host`, `deviceTag`), builds the `new` blob, inserts it,
  and returns the `key` used — callers must persist it for `UnblockDeviceAsync(key)`.
- `RouterCapabilities.SupportsDeviceBlocking = true` for TP-Link, with the read/reconcile gap
  called out in the adapter's own comment.
- If a future live capture confirms the read/list endpoint, blocked-state reconciliation
  (`RouterReportedBlocked` alongside `RouterReportedLimit`) becomes straightforward to add — the
  `Device` aggregate and `PolicyReconciliationService` pattern already exist for exactly this shape
  of problem.
