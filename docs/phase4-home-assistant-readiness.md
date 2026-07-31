# NetPilot — Phase 4 Readiness: Home Assistant Integration

**Status (updated July 31, 2026): Not started — this doc's §3 API contract is still the target design, but `docs/phase5-home-assistant-integration-plan.md` now scopes the actual v1 build down to a subset of it.** Confirmed via code audit: no HTTP endpoints exist in `NetPilot.Agent` yet (still `Host.CreateApplicationBuilder`, no `WebApplication`), no bearer-token auth, no OpenAPI, `RouterSessionManager` doesn't exist. Phase 5's v1 covers §3.1 (system), the device/policy/block/reboot/activity rows of §3.3, and §4's auth — deliberately **not** §3.2 (wireless, since `phase3-wireless-management-plan.md` steps 6–9 aren't built) or §3.4 (SSE push). Treat §3/§4/§5 below as the full target shape; Phase 5 is the sequencing for getting there in two bites instead of one.

**Purpose:** two things. First, a review of the current codebase against what a Home Assistant integration actually demands — what's already right, and what would break. Second, the concrete API contract that **Phase 3 builds** and Phase 4 consumes, so the HA work is writing a Python client against a stable surface rather than co-designing a server.

**Decisions taken with the user:** the HTTP API lives in `NetPilot.Agent`; HA talks to it over **REST via a custom integration** (no MQTT broker).

**Written against commit `7360ea1`.** Companion: `docs/phase3-wireless-management-plan.md`.

---

## 1. Review of the current design against HA's requirements

### 1.1 What already fits, and fits well

**The reconciliation model is the right shape.** `PolicyReconciliationService` is declarative — store desired state, converge, write only the diff, log what happened. That is precisely how Home Assistant expects to interact with a device integration: HA sets a state and retries idempotently; it does not want to issue imperative commands and track their outcomes. Most projects reach this design only after a painful rewrite. NetPilot has it already, and Phase 3 should extend the same pattern to wireless rather than bolting a command queue alongside it.

**The `IsUserConfigured` guard is the correct instinct.** Not writing to the router for state a human never configured is exactly the safety property you want once a *second* automation system starts issuing writes. Keep it, and mirror it for wireless.

**`RouterCapabilities` is the seed of the right contract.** HA integrations that assume every device supports every feature produce broken entities on hardware that doesn't. A capability record that the API serializes lets the integration create only the entities the router genuinely supports. Phase 3 §2.2 widens it and makes it non-positional; that upgrade is worth doing before HA depends on it.

**The `IRouterProvider` seam is real, not decorative.** `NetPilot.Core` genuinely never references `TpLink.Sdk` — verified. That means the HA integration talks to NetPilot generically, and a second router brand later requires zero HA changes. That's the platform positioning actually holding up under inspection.

**The honest doc comments are load-bearing.** `RebootAsync`'s comment says plainly that the endpoint is unverified and why. Nothing else in this repo would have told a reader that. Keep doing it — Phase 3 touches more unverified surface than Phase 2 did.

### 1.2 What blocks or degrades a Home Assistant integration

**(a) There is no HTTP API at all.** `NetPilot.Web` is Blazor Server only (`MapRazorComponents`, no `MapGet`/`MapPost`); `NetPilot.Agent` is a bare `Host` with no server. Nothing external can read or change anything. This is the whole of the Phase 4 blocker and the reason the API is being built in Phase 3.

**(b) Two processes fight over one router login.** `Home.razor` calls `RouterProvider.ConnectAsync` in four separate handlers; `Worker.cs` does the same on its schedule; the router permits exactly one session (`NetPilot_Research_Findings_and_Architecture.md` §3.1 step 8, and `TpLinkRouterClient.LoginAsync`'s own error text). Today the Agent absorbs this by reconnecting next tick. Add HA as a third writer and it becomes a real fault source — particularly for wireless writes, where a half-applied change is materially worse than a retried speed limit. `RouterSessionManager` (Phase 3 §6.1) is the fix and it should land before, not after, the API.

**(c) The poll interval is wrong for automation.** 180 s of latency is invisible for bandwidth policy and unacceptable for "away → WiFi off". HA users read that as a broken integration. Phase 3 §6.2 splits the loop; without it, the HA integration will feel bad no matter how clean the API is.

**(d) Stable identifiers exist for devices, but not yet for anything else.** HA needs a `unique_id` per entity that survives restarts, renames, and reconfiguration. Devices are in good shape: `MacAddress` already normalizes to upper-case dash format (`XX-XX-XX-XX-XX-XX`) and its doc comment explains why. That form just needs to be stated in the API contract as **frozen**, since HA entity IDs derived from it can never change afterward without orphaning every user's automations. Wireless networks have no identifier at all yet — `WirelessNetworkId` in Phase 3 §2 exists for exactly this reason, and its `ToString()` format is a public contract from the first release.

**(e) No authentication story.** The dashboard is unauthenticated on the LAN. An API that can disable the household's WiFi needs a credential, even on a trusted network — if only so an HA misconfiguration can't reach it accidentally. §4 below.

**(f) `GetRouterInfoAsync` returns literal `"unknown (unverified endpoint)"`.** HA's device registry shows model and firmware in the UI. Capture R6 in Phase 3 §1.1 fixes this for one HTTP call's worth of effort.

**(g) Error handling is log-and-continue.** `PolicyReconciliationService` catches write failures and appends to the activity log; `Worker` catches everything and retries. Right for a background loop, wrong for a synchronous API call — an HA service call that returns 200 while the router write silently failed will produce automations that appear to work and don't. API writes must propagate failures as error responses; only the background loop swallows.

**(h) Availability isn't modelled.** `Worker._connected` is a private bool. HA entities need an `available` property so they show as unavailable when the router is unreachable, rather than reporting stale state as current. Promote connection state into something the API can expose, with a `LastSuccessfulPollAtUtc`.

---

## 2. Architecture: where the API sits

```
Home Assistant  ──REST(+token)──┐
                                 ├──►  NetPilot.Agent  ──► RouterSessionManager ──► IRouterProvider ──► Router
NetPilot.Web (Blazor) ──REST────┘         (:8080)                 │
                                             │                     └── single login slot, serialized
                                             └── Worker: slow loop (180s) + fast channel + wireless sweep
                                                        │
                                                   LiteDB (shared volume)
```

The Agent owns the router session and the HTTP surface. The Web dashboard stops calling `IRouterProvider` directly and becomes a client — which incidentally means the dashboard and Home Assistant exercise the same code path, so the API can't rot behind a UI that bypasses it.

LiteDB stays shared, but with a single writer for router-touching operations. Web may keep reading the DB directly for display (it already does, via the injected stores) — reads are harmless and avoid a pointless hop.

---

## 3. API contract (built in Phase 3)

Versioned under `/api/v1/`. JSON, UTC ISO-8601 timestamps, `application/problem+json` (RFC 7807) for errors. Publish OpenAPI via .NET 10's built-in `AddOpenApi()` — the HA integration author (likely future-you) gets a generated reference for free.

### 3.1 System

| Method | Path | Notes |
|---|---|---|
| `GET` | `/health` | Unauthenticated. Liveness only, no router state |
| `GET` | `/api/v1/system/info` | NetPilot version, provider id, router model/firmware, `routerConnected`, `lastSuccessfulPollAtUtc` — drives HA's device registry entry and entity availability |
| `GET` | `/api/v1/system/capabilities` | Serialized `RouterCapabilities`. **HA reads this at setup and creates only supported entities** |

### 3.2 Wireless — the Phase 3 headline

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/v1/wireless` | All networks: observed state, desired state, `features`, `coupledWith`, `revertAtUtc` |
| `GET` | `/api/v1/wireless/{id}` | `id` = `main-2.4g`, `main-5g`, `iot-2.4g`, `guest-5g` |
| `PUT` | `/api/v1/wireless/{id}/desired` | Full desired state. Idempotent — HA re-asserting the same body is a no-op that returns 200 without touching the router |
| `POST` | `/api/v1/wireless/{id}/enable` | Convenience wrapper. Optional `?revertAfter=PT2H` (ISO-8601 duration) |
| `POST` | `/api/v1/wireless/{id}/disable` | Same. `409` + problem-details if it would disable the last remaining network and `AllowDisableAllRadios` is false |

`PUT …/desired` is the primitive; the enable/disable pair exists because HA `switch` entities map onto them cleanly and because `revertAfter` reads better as a verb than as a field.

### 3.3 Devices, policies, usage

| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/v1/devices` | Filterable by `online`, `category` |
| `GET` | `/api/v1/devices/{mac}` | Canonical MAC form, documented and frozen |
| `PUT` | `/api/v1/devices/{mac}/limit` | Per-device override; `null` body clears it back to category policy |
| `GET`/`PUT` | `/api/v1/policies[/{categoryKey}]` | Category policies |
| `GET` | `/api/v1/usage/devices/{mac}` | `?from=&to=&granularity=hour\|day` |
| `GET` | `/api/v1/activity` | Paged activity log — becomes an HA logbook feed |
| `POST` | `/api/v1/router/reboot` | Behind the same confirmation semantics as the dashboard |

### 3.4 Push

`GET /api/v1/events` — Server-Sent Events, emitting device online/offline, wireless state changes, policy applications. Optional for a first HA integration (polling every 30 s is fine and simpler), but it is what makes presence-driven automations feel instant, and SSE costs far less to add to an existing ASP.NET app than a WebSocket protocol. Build the endpoint in Phase 3 even if the HA integration polls at first.

### 3.5 Contract rules that must hold from day one

- **Idempotent writes.** Re-asserting current state returns 200 and performs no router write. HA retries constantly; anything else generates write storms.
- **Errors surface.** A router write failure inside an API request returns 502 with problem-details, never a 200 with a logged error.
- **IDs are frozen.** `{mac}` format and `WirelessNetworkId` strings become HA `unique_id`s. Changing them later orphans every user's entities and automations.
- **Capabilities gate everything.** An unsupported operation returns `501 Not Implemented` with the capability flag named in the response.
- **Additive-only evolution** within `v1`. New fields fine; renames and removals need `v2`.

---

## 4. Authentication

Long-lived bearer tokens, which is what HA config flows expect and what the user will paste once:

- Dashboard generates a token (name + created date + optional expiry); the value is shown **once** and stored as a salted hash in LiteDB. Reuse `RouterPasswordProtector`'s Data Protection setup for consistency, but hash rather than encrypt — the server never needs to read a token back.
- `Authorization: Bearer <token>` on everything except `/health`.
- Bind the Agent's HTTP listener to the LAN interface, not `0.0.0.0`, and document that NetPilot is not intended to be internet-exposed.
- Rate-limit auth failures. Log token use with the token's name in the activity log, so "which client turned the WiFi off" is answerable.

Deliberately **not** doing OAuth or per-user accounts. Single-household, LAN-scoped, one trusted client. Complexity here buys nothing.

---

## 5. Home Assistant entity mapping (Phase 4)

Custom integration at `integrations/homeassistant/custom_components/netpilot/`, HACS-installable, config flow taking host + port + token.

| HA entity | Source | Notes |
|---|---|---|
| `switch.netpilot_wifi_24ghz` / `_5ghz` / `_iot` / `_guest` | `/api/v1/wireless` | The headline. Created only for networks whose `features.canToggle` is true |
| `switch.netpilot_limit_<device>` | per-device override | Toggles the device's speed limit on/off |
| `sensor.netpilot_<device>_usage` | usage endpoints | `device_class: data_size`, `state_class: total_increasing` |
| `device_tracker.netpilot_<device>` | device online state | Presence detection straight off the router — often the single most useful thing NetPilot can give an HA user |
| `binary_sensor.netpilot_router_online` | `system/info` | Drives availability for everything else |
| `sensor.netpilot_devices_online` | device list | Count |
| `button.netpilot_reboot_router` | reboot endpoint | |

Custom services worth exposing: `netpilot.set_device_limit`, `netpilot.set_wireless` (with `revert_after`), `netpilot.apply_policy`.

The automations the user described map directly:

```yaml
# Everyone left → IoT and guest WiFi off, main stays up for the cameras
- trigger: {platform: state, entity_id: group.family, to: not_home}
  action:
    - service: switch.turn_off
      target: {entity_id: [switch.netpilot_wifi_iot, switch.netpilot_wifi_guest]}

# Bedtime → kids' devices throttled, guest WiFi off with a safety revert
- trigger: {platform: time, at: "22:30:00"}
  action:
    - service: netpilot.set_wireless
      data: {network: guest-5g, enabled: false, revert_after: "PT9H"}
```

---

## 6. What Phase 3 must land for Phase 4 to be straightforward

Ordered by how much pain their absence causes:

1. `RouterSessionManager` — one serialized router session. Without it, HA writes race the Agent loop.
2. Agent as `WebApplication` + the §3 endpoints + §4 auth. The literal prerequisite.
3. Wireless desired-state model with stable `WirelessNetworkId`s and per-network `features`.
4. Loop split so writes apply in seconds, not on a 180 s tick.
5. `RouterCapabilities` widened to non-positional, serialized at `/api/v1/system/capabilities`.
6. Availability + `lastSuccessfulPollAtUtc` exposed.
7. `MacAddress`'s existing upper-case-dash format documented in the API contract as frozen.
8. `GetRouterInfoAsync` returning real model/firmware (capture R6).
9. OpenAPI published.

Land those and Phase 4 is a Python client plus entity definitions — a weekend, not a redesign.

---

## 7. Open questions

1. **Does the HA integration poll or subscribe** in its first version? Polling every 30 s is simpler and adequate for everything except presence. Recommendation: ship polling, build the SSE endpoint in Phase 3 anyway, switch later without an API change.
2. **Does the Web dashboard move fully onto the API in Phase 3**, or keep direct store reads for display and use the API only for writes? The hybrid is less work and honestly fine; full migration is cleaner and guarantees the API stays exercised.
3. **Publishing target for the integration** — bundled in this repo, or a separate repo for HACS? Same repo is easier now; HACS default-list submission later wants its own.
4. **Is per-device speed limit worth exposing as an HA switch**, given the category policies already automate it? It's cheap to add and some users will want per-device manual control — but it multiplies entity count on a 26-device network.
