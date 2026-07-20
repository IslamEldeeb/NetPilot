# NetPilot — Phase 2 Investigation + Plan: Per-MAC Usage Tracking

**Status:** Investigation complete against existing docs/code; one live protocol check still recommended before implementation starts (see "Open items" below — same pattern as Phase 1: live checks happen through the user's Cowork session with Chrome connected, not here).

**Goal:** Track bandwidth usage per MAC address and roll it up into accurate monthly totals, despite the router's own usage counter resetting on its own — user has observed this happening (via the TP-Link app) but isn't sure if the trigger is a restart, a daily rollover, or something else. **The design below doesn't need to know the trigger** — see §3.

---

## 1. What Phase 1 already told us (re-read, not re-derived)

The reconciliation loop already polls `admin/smart_network?form=game_accelerator` (`operation=loadDevice`) every 30s for speed-limit enforcement. Per `docs/phase1-live-findings.md`, the raw response for each device includes two fields nobody has used yet:

```json
{
  "...": "...",
  "onlineTime": "...",
  "trafficUsage": "..."
}
```

These were captured in the response shape but their **actual values were never recorded** — the live-findings doc shows them as placeholders. Neither field exists yet in `TpLinkDeviceRecord` (`src/TpLink.Sdk/Models/TpLinkDeviceRecord.cs`) or `RouterDeviceSnapshot` (`src/NetPilot.Abstractions/RouterDeviceSnapshot.cs`), and nothing persists usage today — `DeviceDocument` has no usage/history columns.

Practical implication: **the field already exists on an endpoint NetPilot polls every 30 seconds anyway.** Usage tracking likely doesn't need a new HTTP call or polling path — it needs the existing snapshot to carry two more fields, and a new place to store/accumulate them.

## 2. Open items — a live check sharpens this, but nothing is blocked on it

**Checked against the actual web admin UI (user screenshot, Client Speed Limit table):** the columns shown are Device Info, Interface, Real-time Rate, Speed Limit, Modify — no usage/traffic column. The user's confirmed that per-device usage is only visible in the TP-Link *mobile app*, not this web page.

This doesn't contradict what Phase 1 found. The web table not rendering a usage column says nothing about whether the underlying JSON response carries one — UIs routinely omit fields that a shared API response includes, and `phase1-live-findings.md`'s captured `loadDevice` response already shows `trafficUsage`/`onlineTime` as real keys in that JSON, independent of what this particular table chooses to display. The mobile app's usage view is most likely reading a *different* source anyway — Phase 1 separately noted the app fires `cloud_account:*` calls right after login, i.e. TP-Link Cloud, not the local `smart_network` endpoint the SDK already talks to. So: **treat the field's presence in the local API as already established from Phase 1**, not something that still needs proving. No need to chase the mobile app's cloud-backed view to get there.

What's still worth nailing down, since it directly shapes the data model (not the field's existence):

1. **Format/unit of `trafficUsage`.** Bytes? KB/MB as a number? A pre-formatted string like `"1.2 GB"`? A combined download+upload total, or split like `downloadLimit`/`uploadLimit` are (real field names might be `trafficUsageDown`/`trafficUsageUp` in that case)?
2. **Is `onlineTime` a useful corroborating signal?** Not required, but if it drops alongside `trafficUsage` it's a nice sanity check that a reset really happened rather than a stray bad reading.

**Recommendation:** capture the raw `loadDevice` JSON response body directly (network-request capture, same method as Phase 1) rather than relying on what any web page renders — the web UI was never the source of truth here, the API response is. Write real values to `docs/phase2-live-findings.md` when convenient. Nothing in §3 is gated on this.

## 3. Proposed approach: poll-delta accumulation with reset detection (trigger-agnostic)

The key design choice: **detect resets per-device, from the numbers themselves, without caring what caused them.** Whether the counter goes back to (near) zero because of a full router reboot, a daily rollover the firmware does internally, or something else entirely doesn't matter to the algorithm — a counter that used to be higher and is now lower is a reset, full stop. This is deliberately robust to not knowing the exact trigger, which matches where the investigation currently stands (user isn't sure if it's restart-triggered, daily, or otherwise).

Piggyback on the existing 30s tick — no new polling loop:

1. Each tick, alongside the snapshot already used for speed-limit enforcement, read the device's current raw usage counter.
2. Compare to the last-recorded raw value for **that specific MAC** (comparison is always per-device, never global — so it doesn't matter whether resets happen to one device or all of them at once):
   - **Increased** → delta = current − previous → add delta to the device's running monthly total.
   - **Decreased** → treat as a reset for that device. Start a new baseline: delta = current value (assume the counter started at 0 when whatever-triggered-it happened). Log the reset as an activity event so it's visible, not silent.
   - **Unchanged** → no-op (idle device between polls).
3. This makes NetPilot's *own* recorded monthly total durable no matter when or why the router's raw counter resets — that's the actual Phase 2 deliverable, and it's solved by this loop regardless of the answer to "restart or daily or what."

This is the standard pattern for accumulating monotonic-with-resets counters (same shape as e.g. Prometheus counter handling). The only real requirement is polling often enough that a reset gets caught before too much usage happens invisibly on the new baseline — the existing 30s interval is comfortably frequent enough for that, whether resets happen daily or on an unpredictable restart schedule.

## 4. Data model

Keep this as its own concern, not bolted onto `DeviceDocument` (that document is current-state, not time-series). Two new LiteDB collections, following the existing `IXStore`/`LiteXStore` pattern (`IDeviceStore`/`LiteDeviceStore`, etc.):

```
UsageStateDocument            — one row per MAC, mutable, "where we are right now"
  Mac (BsonId)
  LastRawCounterBytes          // last raw value read from the router, for next delta calc
  LastPollAtUtc
  CurrentMonthKey               // "2026-07", UTC-month by default (confirm w/ user if local tz matters)
  CurrentMonthDownloadBytes
  CurrentMonthUploadBytes       // splits assume #1 above resolves to separate down/up fields;
                                 // collapse to one CurrentMonthBytes if the router only gives a combined total

UsageHistoryDocument           — append-only, one row per Mac+Month, "what the dashboard charts"
  Id = $"{Mac}|{MonthKey}"
  Mac
  MonthKey
  TotalDownloadBytes
  TotalUploadBytes
  FinalizedAtUtc                // set once, when the month rolls over — history rows are immutable after that
```

On each tick, after updating `UsageStateDocument`, check whether `CurrentMonthKey` still matches the current month; if not, finalize the previous month into `UsageHistoryDocument` and reset the running counters for the new month.

## 5. Where this plugs into the existing architecture

Follows the same seams the project already committed to in `mvp-product-architecture.md` — no new architectural concepts, just new implementations of the existing pattern:

- **`RouterDeviceSnapshot`** (`NetPilot.Abstractions`) gains a `UsageInfo` field (raw counter(s) as reported this poll). New `RouterCapabilities.SupportsUsageTracking` flag, same capability-negotiation pattern already used for `SupportsPriorityQos` — so a future router brand that can't report usage degrades honestly instead of guessing.
- **`TpLinkDeviceRecord`** (`TpLink.Sdk`) gains the confirmed field(s) once #1 is resolved live.
- **`TpLinkRouterProvider.ToSnapshot`** maps them through, same as every other field today.
- New **`IUsageStore`** (`NetPilot.Core/Usage/`) + **`LiteUsageStore`** (`NetPilot.Data`), mirroring `IDeviceStore`/`LiteDeviceStore` exactly.
- New **`UsageTrackingService`** (`NetPilot.Core/Usage/`), parallel to `PolicyReconciliationService` — same constructor-injection style, called from `Worker.cs` in the same tick right after (or alongside) `reconciliationService.ReconcileAsync(...)`. It reuses the snapshot already fetched for speed-limit enforcement, so usage tracking adds no extra HTTP calls to the router. Reset-detection logic lives here, not in the provider — `TpLinkRouterProvider` stays a dumb translator, consistent with the project's stated "thin adapter" principle.
- Unit tests: `UsageTrackingServiceTests` against a fake provider/store, same style as `PolicyReconciliationServiceTests` — the delta/reset math is fully testable without a live router (reset detection especially deserves table-driven tests: increase, decrease, near-simultaneous multi-device drop, month rollover, missed-poll gap).

## 6. Dashboard addition

Fifth panel — "Usage" — on `NetPilot.Web`:

- Per-device current-month total (download/upload if the router splits them), sorted descending — reuses the existing device-grouping UI pattern from the Connected Devices panel.
- A simple per-device history view (last N months from `UsageHistoryDocument`) — table is enough for v1, matches the project's MVP-first bias; `mvp-product-architecture.md` §10 already flagged "historical usage charts" as a natural future add once storage exists, so this is that.

## 7. Risks / edge cases to design around explicitly

- **Missed polls** (Agent down for a while, including across a reset): the next successful poll still correctly triggers reset detection (counter is lower than last-known), but any usage that happened *before* the reset but *after* the last successful poll is lost — bounded, acceptable, worth a one-line note in the doc/README rather than silently pretending totals are exact.
- **Month boundary:** default to UTC month; flag to the user that this may not match their intuitive billing-cycle month if their ISP's cycle doesn't start on the 1st — cheap to make configurable later, not worth over-building now.
- **Frequent resets (e.g. if it turns out to be daily, not just on restart):** no special handling needed — the per-tick delta/reset loop in §3 doesn't care how often resets happen, it just needs to see each one before too much usage accumulates on the new baseline. Even a reset every few hours is fine at a 30s poll interval.
- **Poll-boundary error:** ~30s of imprecision around a reset event is expected and fine — the goal is monthly totals, not billing-grade metering.

## 8. Suggested execution order

1. **Optional: capture real `trafficUsage`/`onlineTime` values** from the raw `loadDevice` API response (network capture, not the web UI — that page doesn't render usage at all, confirmed). Sharpens the data model in §4 but doesn't block starting §2–§3 below.
2. Extend `TpLinkDeviceRecord` + `RouterDeviceSnapshot` with the confirmed field(s).
3. Add `IUsageStore`/`LiteUsageStore` + `UsageTrackingService`; unit-test delta/reset logic against a fake provider — no live router needed for this part.
4. Wire into `Worker.cs`'s existing tick.
5. Dashboard panel.
6. Deploy, let it run a real month, sanity-check totals against another source if one's available (router's own Tether app display, or ISP portal) before trusting it fully.

---

**Bottom line:** the mechanism (per-device poll-delta accumulation with trigger-agnostic reset detection) is the well-understood, standard way to handle this class of problem, and it fits into NetPilot's existing architecture with no new concepts — new store, new service, one new snapshot field, called from the tick that already runs. It solves "the counter resets and I don't know exactly when or why" by construction — nothing here is blocked on figuring out whether it's a restart, a daily rollover, or something else. The only thing worth a live check before writing code is the exact shape of the `trafficUsage` field itself.
