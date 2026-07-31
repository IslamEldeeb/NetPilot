# NetPilot — Phase 3 Plan: Wireless Management + Automation Foundations

**Implementation status (as of July 25, 2026):** §0's gate is closed — `admin/wireless` confirmed plain JSON, no envelope, see `docs/phase3-live-findings.md`. Steps 1–5 of §9's build order are done: `RouterCapabilities` widened, `Wireless.cs` abstractions, `TpLink.Sdk` wireless read DTOs/methods, `TpLinkRouterProvider.GetWirelessNetworksAsync` (all 6 networks, real). Writes are live for **guest** (SSID/password/enable) and **IoT** (SSID/enable, password not yet) — `ApplyWirelessAsync` throws for `main` and for channel/width/TX-power on any network, all deliberately, pending their own live capture. **One design correction from what's written below:** §2's `WirelessNetworkUpdate` doc comment assumes providers "must read-modify-write" — live capture disproved this. Writes are a true partial delta (only changed fields sent; omitted fields are left alone, not blanked), confirmed independently for guest (an SSID-only write) and IoT (real user-captured traffic). No read-modify-write merge exists or is needed in `TpLinkRouterProvider`. A basic read-only+partial-edit `WirelessTab` exists inline in `Home.razor` (not the extracted-component structure §7/step 10 describes). Steps 6–9 (`NetPilot.Data`, `NetPilot.Core/Wireless` reconciliation, `RouterSessionManager`, Agent HTTP API) are **not started** — the Web dashboard still calls `IRouterProvider` directly, same "two-sessions" pattern the plan below already flags as a known issue.

**Re-confirmed by code audit, July 31, 2026 — still accurate, nothing above has changed.** One scope note since this was written: `docs/phase4-home-assistant-readiness.md` (the API contract this doc's steps 6–9 feed) now has a companion, `docs/phase5-home-assistant-integration-plan.md`, which sequences a narrower v1 API (devices/policies/block-list/reboot only, no wireless endpoints) ahead of this doc's steps 6–9 — so `RouterSessionManager` and the Agent HTTP host will likely land first as part of that narrower effort, then get extended with wireless endpoints once steps 6–7 here are done. Doesn't change any of the wireless-specific design below, just the delivery order.

**Scope decided with the user:** full wireless management (radio on/off, SSID, password, channel/width/TX power, schedule) across the main 2.4 GHz, main 5 GHz, IoT, and Guest networks — plus the architectural groundwork that Phase 4 (Home Assistant integration) needs.

**Read first:** `docs/phase1-live-findings.md` (confirmed protocol ground truth), `docs/mvp-product-architecture.md` (the blueprint), `docs/phase2-implementation-plan.md` (the pattern this doc follows). Companion doc: `docs/phase4-home-assistant-readiness.md` — the API/contract half of this phase, split out because it's a different audience.

**Written against the code as of commit `7360ea1`.** Reviewed: `IRouterProvider`, `RouterCapabilities`, `SpeedLimit.cs`, `TpLinkRouterClient`, `TpLinkTransport`, `TpLinkRouterProvider`, `PolicyReconciliationService`, `RouterProviderRegistry`, `Agent/Worker.cs`, `Agent/Program.cs`, `Web/Program.cs`, `Web/Components/Pages/Home.razor`, `NetPilot.Data/*`, `test/**`.

---

## 0. The one thing that gates everything

Phase 1 confirmed the login handshake and the `admin/smart_network` read/write path are **plain JSON, no AES envelope**. Wireless lives in a different section — `admin/wireless` — which `TpLinkTransport`'s own header comment explicitly calls out as *never live-verified*, and which `NetPilot_Research_Findings_and_Architecture.md` §3.1 describes as potentially needing the RSA-signed / AES-encrypted envelope that was deliberately not implemented.

**The evidence leans strongly toward "it's plain JSON too"** — this firmware (`1.7.1 Build 20260213`) turned out to use a simplified handshake that neither documented OSS variant matches, and both the login response and `smart_network` responses came back unencrypted. If the firmware had a per-section envelope mode we'd expect login itself to use it. But "leans toward" is not "confirmed", and the cost of guessing wrong here is a bricked WiFi config, not a failed unit test.

**So Phase 3 starts with a live capture session, not with code.** Section 1 is the capture checklist. Everything from Section 2 onward is written to be implementable the moment those captures land, and is deliberately structured so that if the envelope *is* required, only `TpLinkTransport` changes — not the abstractions, not Core, not the API.

---

## 1. Live capture checklist (Claude in Chrome, user's network, before writing SDK code)

Same method as Phase 1: user authenticates themselves, instrumented `fetch`/`XHR` capture, **read operations freely, write operations one at a time with the user watching**. Capture request URL, body, and full response for each.

### 1.1 Reads — safe, do all of these first

| # | UI path | Expected endpoint | What we need out of it |
|---|---|---|---|
| R1 | Wireless → Wireless Settings (page load) | `admin/wireless?form=wireless_2g` + `wireless_5g`, `operation=read` | Exact field names for enable flag, SSID, security mode, PSK (is it returned? masked? omitted?), channel, bandwidth, TX power |
| R2 | Wireless → IoT Network | unknown — likely `admin/wireless?form=wireless_iot` or a `_2g`-suffixed variant | **The IoT form name is the single biggest unknown.** `deviceTag: "iot_2.4G"` in the Phase 1 device capture proves the network exists; nothing tells us its config form name |
| R3 | Wireless → Guest Network | likely `admin/wireless?form=wireless_guest_2g` / `_5g` | Same fields as R1, plus whatever "allow guests to access my local network" maps to |
| R4 | Any page (background poll) | `admin/status?form=all` | §3.3 claims this returns "WiFi toggles" — if true it's a cheap single-call read for all radio states, better than 4 separate form reads per tick |
| R5 | Wireless → Wireless Schedule (if present on this firmware) | unknown | Whether the router has its own schedule engine that would fight NetPilot's desired state |
| R6 | Advanced → System → Firmware Upgrade | `admin/firmware?form=upgrade` | Fixes `GetRouterInfoAsync` returning `"unknown (unverified endpoint)"` — free win while we're in there |
| R7 | Advanced → System → Reboot (page load only, do not click) | — | Confirms whether `admin/system?form=reboot` is the real endpoint, since `RebootAsync` currently ships unverified |

### 1.2 Writes — one at a time, each with a read-back

| # | Action | Why it's the specific action to capture |
|---|---|---|
| W1 | Toggle 5 GHz off, then on | 5 GHz first because the user's phone/laptop can fall back to 2.4 GHz — lowest lockout risk. Shows the enable field's on-the-wire value (`"on"`/`"off"`? `1`/`0`? `true`?) |
| W2 | Change 5 GHz channel (a no-op-ish change), save | **The critical one: is a write a full-object PUT or a partial PATCH?** Whether the body contains SSID and PSK alongside the changed field decides the entire provider write strategy |
| W3 | Toggle IoT network off, then on | Confirms the IoT form and — separately — whether the 2.4 GHz radio state changes with it |
| W4 | Toggle 2.4 GHz off while IoT is on | **Answers the shared-radio question**: does killing the 2.4 GHz radio also kill the IoT SSID? If yes, "turn off 2.4 GHz" and "turn off IoT" are not independent operations and the UI/HA must say so |
| W5 | Change the guest SSID | Confirms the guest write path and whether SSID changes force-disconnect clients |

### 1.3 Questions each capture must answer explicitly

Write these into `docs/phase3-live-findings.md` as a findings doc, mirroring `phase1-live-findings.md`:

1. **Envelope or plain?** If any `admin/wireless` response body is base64/opaque rather than readable JSON, stop and re-plan — that's the AES path and it's a separate work item.
2. **Whole-object write?** If W2's body includes SSID/PSK, every write must be read-modify-write. Assume yes until proven otherwise.
3. **Is the PSK readable on read?** If the router returns the real PSK, NetPilot can round-trip it without ever storing it. If it returns a mask (`"********"`), a whole-object write would overwrite the real password with the mask — a config-destroying bug. This determines whether SSID/channel edits are safe at all without also re-supplying the password.
4. **Shared radio semantics** (W3/W4) — are IoT and main 2.4 GHz independent?
5. **Session survival** — does toggling a radio invalidate the `stok`, force a re-login, or return before the change is actually applied? If the router restarts the wireless subsystem, expect a delay before a read-back reflects reality.
6. **Does a wireless write disturb the Speed Limit config?** Read `smart_network?form=game_accelerator` before and after W1 and diff. Cheap paranoia, high value.

---

## 2. `NetPilot.Abstractions` — new wireless contract

New file `src/NetPilot.Abstractions/Wireless.cs`:

```csharp
namespace NetPilot.Abstractions;

public enum WirelessBand { Ghz24, Ghz5, Ghz6 }

public enum WirelessNetworkKind { Main, Guest, Iot }

/// <summary>
/// Stable, provider-independent identifier for one wireless network. Used as the LiteDB key,
/// the REST path segment, and the Home Assistant unique_id — so it must never change shape
/// once shipped. Format: "{kind}-{band}", lowercase: main-2.4g, main-5g, iot-2.4g, guest-5g.
/// </summary>
public readonly record struct WirelessNetworkId(WirelessNetworkKind Kind, WirelessBand Band)
{
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}-{BandToken(Band)}";
    public static string BandToken(WirelessBand b) => b switch
    {
        WirelessBand.Ghz24 => "2.4g",
        WirelessBand.Ghz5 => "5g",
        WirelessBand.Ghz6 => "6g",
        _ => "unknown"
    };
    public static WirelessNetworkId Parse(string id) { /* inverse, throws on unknown */ }
}

/// <summary>What the router currently reports. Read-only observation, never intent.</summary>
public record WirelessNetworkState(
    WirelessNetworkId Id,
    string DisplayName,
    bool Enabled,
    string? Ssid,
    bool? SsidBroadcast,
    string? SecurityMode,
    int? Channel,
    string? ChannelWidth,
    string? TxPower,
    WirelessNetworkFeatures Features,
    IReadOnlyList<WirelessNetworkId> CoupledWith);

/// <summary>
/// Per-network capability flags — not per-router. The AX53's guest network may allow an
/// SSID change but not a channel change; HA must only create entities for what's writable.
/// CoupledWith on the state above carries the W4 finding: toggling this network also
/// affects those. Surfaced to the UI and API so "turn off 2.4 GHz" can warn that IoT
/// goes with it, instead of the user discovering that when their sensors drop off.
/// </summary>
public record WirelessNetworkFeatures(
    bool CanToggle,
    bool CanEditSsid,
    bool CanEditPassword,
    bool CanEditChannel,
    bool CanEditChannelWidth,
    bool CanEditTxPower,
    bool CanEditBroadcast);

/// <summary>
/// A sparse delta. Null means "leave alone" — never "clear". Providers must read-modify-write
/// so a null here can never blank a field on a whole-object firmware write.
/// </summary>
public record WirelessNetworkUpdate
{
    public bool? Enabled { get; init; }
    public string? Ssid { get; init; }
    public string? Password { get; init; }
    public bool? SsidBroadcast { get; init; }
    public int? Channel { get; init; }
    public string? ChannelWidth { get; init; }
    public string? TxPower { get; init; }
}
```

### 2.1 `IRouterProvider` additions

```csharp
Task<IReadOnlyList<WirelessNetworkState>> GetWirelessNetworksAsync(CancellationToken ct);

/// <summary>
/// Applies a sparse update. Implementations MUST read current config and merge before
/// writing if the underlying firmware only accepts whole-object writes (see phase3 capture
/// W2). Returns the router's state after the write, re-read — not an echo of the request.
/// </summary>
Task<WirelessNetworkState> ApplyWirelessAsync(WirelessNetworkId id, WirelessNetworkUpdate update, CancellationToken ct);
```

Returning the re-read state (rather than `Task`) is a deliberate change from how `SetSpeedLimitAsync` was done. The Speed Limit write returns a bare `{"success":true}` with no echo, which forced callers to re-poll; wireless writes are user-facing and interactive, so the provider owns the read-back and the caller gets truth immediately.

### 2.2 `RouterCapabilities` — breaking-change note

`RouterCapabilities` is a positional record with six parameters, constructed in exactly one place (`TpLinkRouterProvider.Capabilities`) and consumed in one (`PolicyReconciliationService.ResolveCategoryKey`). Adding wireless flags positionally is safe today but gets worse with every provider added.

**Change it to init-only properties with defaults now, while there's one implementation:**

```csharp
public record RouterCapabilities
{
    public bool SupportsSpeedLimit { get; init; }
    public bool SupportsDeviceCategorization { get; init; }
    public bool SupportsPriorityQos { get; init; }
    public bool SupportsGuestNetworkInfo { get; init; }
    public bool SupportsUsageTracking { get; init; }
    public bool SupportsReboot { get; init; }
    public bool SupportsWirelessRead { get; init; }
    public bool SupportsWirelessToggle { get; init; }
    public bool SupportsWirelessEdit { get; init; }
    public bool SupportsWirelessSchedule { get; init; }
}
```

Cost: one edit at each of the two sites. Benefit: every future provider declares only what it supports, and Phase 4's `GET /api/v1/system/capabilities` serializes this record directly as the contract that tells Home Assistant which entities to create.

---

## 3. `TpLink.Sdk` — protocol layer

New folder `src/TpLink.Sdk/Wireless/`:

- **`TpLinkWirelessForm.cs`** — the form-name constants, filled in from the captures. One place to fix if a firmware revision moves them.
- **`TpLinkWirelessConfig.cs`** — DTO per form, using the existing `LenientStringConverter`/`LenientIntConverter` from `Models/`. Phase 1 proved this firmware mixes `"5120"` and `5120` for the same field; assume the same for channel and TX power.
- **`TpLinkRouterClient`** gains:
  ```csharp
  Task<TpLinkWirelessConfig> GetWirelessAsync(string form, CancellationToken ct);
  Task WriteWirelessAsync(string form, TpLinkWirelessConfig config, CancellationToken ct);
  ```
  `WriteWirelessAsync` takes a **complete config object**, not a delta — merging is the provider's job, and keeping the SDK dumb here means it stays a faithful, independently-publishable mirror of the wire protocol.

**Preserve the doc-comment discipline already in this codebase.** `RebootAsync` and `GetRouterInfoAsync` both carry honest "this is unverified, here's exactly why, live-check before trusting" comments. Any wireless method that ships ahead of a capture gets the same treatment, and any that ships after gets a comment citing the specific capture ID (W1–W5) that confirmed it.

---

## 4. `NetPilot.Core/Wireless/` — desired state and reconciliation

This is where the Home Assistant story is actually won. HA automations are declarative and retry-happy: "when nobody is home, WiFi off." The right server-side model is therefore **desired state that a loop converges on**, not fire-and-forget commands — exactly the pattern `PolicyReconciliationService` already implements for speed limits. Reuse it rather than inventing a second one.

### 4.1 Files

| File | Purpose |
|---|---|
| `WirelessDesiredState.cs` | Per-network intent: `Id`, `DesiredEnabled` (`bool?`), `DesiredSsid`, `DesiredChannel`, …, `IsUserManaged`, `Source` (`Dashboard`/`Api`/`Schedule`), `UpdatedAtUtc`, `RevertAtUtc` |
| `IWirelessStore.cs` | `GetAllAsync` / `FindAsync(id)` / `UpsertDesiredAsync` / `UpsertObservedAsync` |
| `WirelessReconciliationService.cs` | The loop: read observed → compare → write only what's wrong → log |
| `WirelessFingerprint.cs` | Same trick as `PolicyFingerprint` — cheap comparable hash of the desired tuple so an unchanged tick sends zero writes |

### 4.2 Reconciliation rules (each one exists because of a specific failure mode)

1. **Adopt on first read, never write.** Copy `PolicyReconciliationService`'s `IsUserConfigured` guard verbatim in spirit: if `IsUserManaged` is false, record what the router says and write nothing. The comment already in that file spells out the disaster this prevents — a fresh/empty LiteDB (say, running locally against the same router while the Proxmox deployment holds the real config) silently pushing defaults over live settings. For speed limits that costs a wrong bandwidth cap. For wireless it costs the household's WiFi.

2. **Write only the diff.** Fingerprint compare before any router call, same as policies. A router that's already in the desired state gets zero writes, however often HA re-asserts.

3. **Drift correction is opt-in per network.** If someone changes SSID in the router's own UI, NetPilot could reassert its desired value on the next tick — correct behaviour for an enforcement engine, infuriating behaviour for a human who just changed it deliberately. Ship `EnforceDrift` as a per-network flag, **default off for edit fields, default on for the enable flag**, since the enable flag is the one HA actually automates.

4. **Auto-revert (`RevertAtUtc`).** `POST /api/v1/wireless/main-5g/disable?revertAfter=PT30M` records the previous state and restores it at expiry. This is the dead-man switch that makes remote WiFi control safe: a bad automation, a phone tapped by a kid, or an HA outage mid-automation can't leave the house without WiFi indefinitely. It also directly serves the "guest WiFi for 2 hours" and "IoT off overnight" cases without a scheduler.

5. **Lockout guard.** Refuse to disable *every* wireless network in one operation unless `NetPilot:Wireless:AllowDisableAllRadios` is explicitly true (default false). The Agent is wired on CT 109 so it can't lock itself out, but the person holding the phone can. Return `409 Conflict` with a problem-details body explaining which network would be the last one standing.

6. **Coupled networks are surfaced, not hidden.** If W4 shows IoT rides the 2.4 GHz radio, `CoupledWith` is populated and both the dashboard and the API response say so. Do not silently cascade — say what will happen and let the caller confirm.

### 4.3 Activity log

Add to `ActivityEventType` (which today ends at `RouterRebooted`): `WirelessAdopted`, `WirelessApplied`, `WirelessSkippedAlreadyCorrect`, `WirelessDriftDetected`, `WirelessAutoReverted`, `WirelessWriteFailed`.

`ActivityLogEntry` is `(DateTimeOffset AtUtc, ActivityEventType Type, MacAddress? Mac, string Message)` — the subject slot is typed `MacAddress?`, so a wireless event has no way to say which network it was about. It's already nullable, so wireless events *compile* with `null` there, but then the activity log can't tell 2.4 GHz from guest. Add a nullable `string? SubjectKey` rather than widening `Mac` to a string: `Mac` being strongly typed is what makes device lookups from the log safe, and a `MacAddress?` field that sometimes holds `main-5g` is the kind of thing that reads fine today and confuses everyone in six months. `LiteActivityLogStore` / `ActivityLogDocument` need the extra field, and old documents deserialize with it null — no migration required.

---

## 5. `NetPilot.Data`

- `Documents/WirelessStateDocument.cs` — one document per `WirelessNetworkId`, holding both observed and desired blocks plus `RevertAtUtc` and the pre-revert snapshot.
- `LiteWirelessStore.cs`, registered in `NetPilotDatabase` and `ServiceCollectionExtensions` alongside the existing stores.
- **Never persist a WiFi PSK.** Even encrypted with `RouterPasswordProtector`, storing the household WiFi password buys nothing: password changes are write-through one-shot operations, and a desired-state loop has no reason to re-assert a password. Store `PasswordLastSetAtUtc` only. If capture question 1.3.3 shows the router masks the PSK on read *and* takes whole-object writes, then a password is required on every SSID/channel edit — in which case the API demands it per-request and it stays in memory only. Say so explicitly in the UI rather than quietly caching a secret.

---

## 6. `NetPilot.Agent` — the structural change

Three problems in the current Worker that Phase 3 has to fix, and all three have the same root:

### 6.1 The two-sessions bug (existing, latent, will bite in Phase 4)

`Web/Components/Pages/Home.razor` injects `IRouterProvider` and calls `RouterProvider.ConnectAsync(...)` directly in `TestConnectionAsync`, `RefreshFromRouterAsync`, `ReapplyPolicyAsync`, and `RebootRouterAsync`. `Agent/Worker.cs` does the same on its own schedule. `TpLinkRouterClient.LoginAsync`'s own error message names the problem — *"another session may hold the router's single login slot"* — and §3.1 step 8 confirms the router allows exactly one active session.

Today this is survivable: the Agent catches the failure, logs, and reconnects next tick. Once Home Assistant is issuing writes on its own cadence, three actors fighting over one login slot becomes a genuine reliability problem, and a WiFi write that dies mid-flight is worse than a speed-limit write that dies mid-flight.

**Fix: `RouterSessionManager` in `NetPilot.Core`** — owns the single `IRouterProvider`, serializes all access behind a `SemaphoreSlim(1,1)`, re-logs-in on session-expiry errors, exposes `ExecuteAsync<T>(Func<IRouterProvider, Task<T>>)`. Everything that touches the router goes through it. The Web dashboard stops touching `IRouterProvider` and calls the Agent's API instead (§6.3).

### 6.2 One tick rate doesn't fit two jobs

Reconciliation polls at 180 s by default. That's fine for speed limits and usage accumulation. It is far too slow for "I left the house, turn off WiFi" — a two-to-three-minute lag makes the feature feel broken.

**Split the loop:**
- **Slow loop, 180 s** — `PolicyReconciliationService` + `UsageTrackingService`, unchanged.
- **Fast path, event-driven** — an in-process `Channel<RouterWorkItem>`. API writes enqueue and the worker drains immediately; wireless desired-state converges in under a second, not on the next slow tick.
- **Wireless sweep, ~30 s** — a cheap safety net that catches drift, expired `RevertAtUtc` timers, and anything the fast path dropped. If capture R4 shows `admin/status?form=all` returns all radio toggles in one call, this is a single request per sweep.

### 6.3 The Agent has to host HTTP

Per the decision made with the user, the Phase 4 API lives in the Agent, because the Agent owns the router session — HA commands then execute immediately with no queue hop and no second login.

Convert `Agent/Program.cs` from `Host.CreateApplicationBuilder` to `WebApplication.CreateBuilder`, keep `AddHostedService<Worker>()`, add minimal APIs, listen on `:8080` inside the container. `Dockerfile.agent` and `deploy/docker/compose` gain a port mapping. The Web dashboard becomes a client of that API.

**This is the biggest single change in Phase 3 and the one most worth landing early**, because both the WiFi UI and the whole of Phase 4 sit on top of it. The full endpoint contract is specified in `docs/phase4-home-assistant-readiness.md` §3 — build it in this phase, consume it from HA in the next.

---

## 7. `NetPilot.Web` — WiFi tab

`Home.razor` is 787 lines holding five tabs, all their state, and all their router calls. Adding a sixth tab with per-network forms, confirmation modals, and coupled-network warnings is the point where that stops being tenable.

**Extract tabs into components first** (`Components/Tabs/DevicesTab.razor`, `PoliciesTab.razor`, `ActivityTab.razor`, `UsageTab.razor`, `ManagementTab.razor`), then add `WirelessTab.razor`. Mechanical, low-risk, and it makes the WiFi tab reviewable on its own.

The WiFi tab itself: a card per wireless network showing observed state, an enable toggle, an edit form gated on `WirelessNetworkFeatures`, a "revert in N minutes" control, and an explicit warning banner when `CoupledWith` is non-empty. Disabling any network requires confirmation, same pattern as the existing reboot confirm.

---

## 8. Tests

Mirror the existing structure — `test/NetPilot.Core.Tests/Wireless/`:

- `WirelessReconciliationServiceTests` — adopt-on-first-read writes nothing; unchanged desired state writes nothing; changed desired state writes once; write failure logs and doesn't corrupt stored state; `RevertAtUtc` restores the prior state exactly once; lockout guard rejects disabling the last network.
- Extend `test/NetPilot.Core.Tests/Fakes/FakeRouterProvider.cs` with the two new interface members, recording calls so "wrote exactly once" is assertable.
- `test/TpLink.Sdk.Tests/WirelessParsingTests.cs` — fixtures pasted verbatim from the live captures, same as `ModelParsingTests` did for devices.
- `test/NetPilot.Data.Tests` — round-trip `WirelessStateDocument`, and confirm an existing DB opens cleanly with the new collection absent.
- API contract tests via `WebApplicationFactory` against the Agent — auth rejection, idempotent re-assert, capability gating.

---

## 9. Build order

1. Live captures (§1) → `docs/phase3-live-findings.md`. **Blocking. Done for reads (all forms) and writes for guest + IoT (enable/SSID/broadcast). Main writes and IoT password writes still open.**
2. ~~`RouterCapabilities` → init-only properties; add wireless flags.~~ **Done.**
3. ~~Abstractions: `Wireless.cs` + two `IRouterProvider` members. Update `FakeRouterProvider`.~~ **Done.**
4. ~~`TpLink.Sdk` wireless DTOs + client methods.~~ **Done for reads (all networks) and writes (guest, IoT). Not done for main.**
5. `TpLinkRouterProvider` — ~~read-modify-write merge~~ (turned out unnecessary — partial writes work), `Features`/`CoupledWith` population, capability flags. **Done**, except `CoupledWith` stays empty everywhere (W4 never ran) and main writes still throw.
6. `NetPilot.Data` — document + store + registration. **Not started.**
7. `NetPilot.Core/Wireless` — desired state, fingerprint, reconciliation service, activity events. Tests alongside. **Not started.**
8. `RouterSessionManager` + Agent loop split. Migrate existing calls onto it. **Not started** — Web still calls `IRouterProvider` directly (the "two-sessions bug" in §6.1 is live, not just theoretical, as of the wireless captures: it fired mid-session against the production Agent).
9. Agent → `WebApplication`; implement the §3 API from the Phase 4 doc; port mapping in Docker assets. **Not started.**
10. Web: extract tabs, add `WirelessTab.razor`, move Web's direct router calls onto the API client. **Partial** — a Wireless tab exists and works (read + guest/IoT partial edit), but inline in `Home.razor`, not extracted into its own component, and still calling `IRouterProvider` directly rather than an API client (steps 6–9 don't exist yet for it to call).
11. Update `CLAUDE.md` / `AGENTS.md` (both currently claim implementation hasn't started — stale since Phase 1) and `docs/deployment.md` for the new port. **Not started.**

Steps 2–7 are pure-logic and testable without the router. Steps 8–10 need a deploy to CT 109 to validate.

---

## 10. Open questions for the user

1. **Who owns the WiFi schedule?** If the router has its own schedule engine (capture R5), NetPilot asserting desired state every 30 s will fight it. Cleanest answer is NetPilot owns scheduling and the router's schedule stays off — but that's a call, not a default.
2. **Should the guest network get a "for N hours" first-class flow** in the UI, or is generic `RevertAtUtc` enough?
3. ~~**Password-change UX** depends entirely on capture question 1.3.3.~~ **Resolved: PSK reads back cleartext, unmasked** (`docs/phase3-live-findings.md`). No re-supply needed — the form only sends a password field when the user is actually changing it, confirmed live for guest.
4. **Drift enforcement default for the enable flag** — proposed on. If the user turns WiFi back on at the router while an HA automation says off, NetPilot would turn it off again within 30 s. Defensible, but should be a deliberate choice.
