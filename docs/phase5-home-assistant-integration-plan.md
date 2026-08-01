# NetPilot — Phase 5 Plan: Home Assistant Integration (MVP scope)

**Date:** July 31, 2026
**Status:** Proposal — awaiting approval before code.
**Scope decided with the user:** ship Home Assistant integration against features that are *already fully implemented* today — devices, speed-limit policies, per-device block/unblock, reboot, router info, activity log. Wireless control (radio/guest-network toggles) is a deliberate fast-follow, not part of this phase — its storage and reconciliation layer (`docs/phase3-wireless-management-plan.md` §4–§6, steps 6–9) doesn't exist in code yet and is a materially bigger build. **Home Assistant runs on the same LAN as CT 109** (confirmed with the user), so it can reach the Agent directly once the API exists; testing the HA side happens in the user's own environment, same constraint as the router itself — this sandbox can't reach either.

**Read first:** `docs/phase4-home-assistant-readiness.md` (the full API contract and HA entity mapping this plan narrows down for v1), `docs/phase3-wireless-management-plan.md` (why wireless is deferred), `docs/mvp-product-architecture.md` (baseline architecture).

**Written against commit `1e4c5dc`.** Verified by code audit at the time: `RouterSessionManager` doesn't exist; `NetPilot.Agent/Program.cs` is `Host.CreateApplicationBuilder`, no HTTP surface; `NetPilot.Web/Home.razor` calls `IRouterProvider` directly; `RouterCapabilities`, device blocking, and real `GetRouterInfoAsync` are already done and reusable as-is. **Update July 31, 2026: `RouterSessionManager` now exists — see Step 1 below.** The rest of this paragraph describes the state before that.

---

## 1. Why this scope, in this order

Two things block *any* HA integration regardless of feature scope, so they come first no matter what:

1. ~~**The two-session bug is real, not theoretical.**~~ **Fixed July 31, 2026** — see Step 1. `Home.razor` and `Worker.cs` used to call `RouterProvider.ConnectAsync` independently; the router allows exactly one login, and this already misbehaved today with two actors (Web + Agent). `RouterSessionManager` closes it before HA becomes a third writer.
2. **There is no HTTP surface at all.** `NetPilot.Agent` is a bare `Host`. Until it's a `WebApplication` with endpoints and auth, nothing external — HA or otherwise — can reach NetPilot. **Still the remaining blocker.**

Everything else (which endpoints, which HA entities) is scoped down from `phase4-home-assistant-readiness.md` §3/§5 to only what's already backed by working code: `IRouterProvider.GetDevicesAsync`, `SetSpeedLimitAsync`, `BlockDeviceAsync`/`UnblockDeviceAsync`, `GetRouterInfoAsync`, `RebootAsync`, plus the existing `ActivityLog` and `DevicePolicy` stores. No wireless endpoints, no `WirelessNetworkId`, no reconciliation service in this phase.

---

## 2. Build order

### Step 1 — `RouterSessionManager` (`NetPilot.Core`) — **done July 31, 2026**
Single owner of the `IRouterProvider` connection, serialized behind a `SemaphoreSlim(1,1)`, re-logs-in on session-expiry, exposes `ExecuteAsync<T>(Func<IRouterProvider, Task<T>>)` plus a `TestConnectionAsync` overload for the dashboard's unsaved-settings test flow. `Worker.cs` and all eight of `Home.razor`'s router call sites route through it now — see `src/NetPilot.Core/RouterConnection/RouterSessionManager.cs` and its tests. Landed ahead of the rest of this phase, exactly as planned, since it fixes a live bug independent of HA.

### Step 2 — `NetPilot.Agent` becomes a `WebApplication`
- Convert `Program.cs` from `Host.CreateApplicationBuilder` to `WebApplication.CreateBuilder`; keep `AddHostedService<Worker>()` as-is.
- Listen on `:8080` inside the container, bound to the LAN interface (not `0.0.0.0` — per `phase4-home-assistant-readiness.md` §4).
- Add `AddOpenApi()` / publish the OpenAPI doc.

### Step 3 — Bearer token auth
- Dashboard-generated long-lived token: name + created date + optional expiry, shown once, stored as a salted hash in LiteDB (new `ApiTokenDocument`/`LiteApiTokenStore`). Reuse the existing Data Protection setup pattern from `RouterPasswordProtector` for consistency, but hash, don't encrypt.
- `Authorization: Bearer <token>` middleware on everything except `/health`.
- Log token use (by name) to the activity log — "which client did X" stays answerable.

### Step 4 — `/api/v1` endpoints (MVP subset of the full contract)

| Method | Path | Backed by |
|---|---|---|
| `GET` | `/health` | unauthenticated liveness only |
| `GET` | `/api/v1/system/info` | `GetRouterInfoAsync`, connection/poll state |
| `GET` | `/api/v1/system/capabilities` | serialized `RouterCapabilities` (already widened, already includes `SupportsDeviceBlocking` etc.) |
| `GET` | `/api/v1/devices` | `IDeviceStore`, filterable by `online`/`category` |
| `GET` | `/api/v1/devices/{mac}` | same store, canonical MAC form (frozen, per existing doc) |
| `PUT` | `/api/v1/devices/{mac}/limit` | `SetSpeedLimitAsync` — per-device override, `null` clears back to category policy |
| `POST` | `/api/v1/devices/{mac}/block` | `BlockDeviceAsync` |
| `POST` | `/api/v1/devices/{mac}/unblock` | `UnblockDeviceAsync` |
| `GET`/`PUT` | `/api/v1/policies[/{categoryKey}]` | `IPolicyStore` |
| `GET` | `/api/v1/activity` | paged `ActivityLogEntry` — becomes the HA logbook feed |
| `POST` | `/api/v1/router/reboot` | `RebootAsync`, same confirmation semantics as the dashboard |

Contract rules carried over unchanged from the existing doc: idempotent writes, errors surface as `502` + problem-details (never a swallowed 200), IDs frozen, additive-only evolution within `v1`. `GET /api/v1/wireless*` and `/api/v1/events` (SSE) are explicitly **out** of this phase — added when Phase 3 steps 6–9 land.

### Step 5 — `NetPilot.Web` migration
Hybrid, not full rewrite (per the existing doc's own recommendation): Web keeps direct LiteDB reads for display, but writes (limit, block/unblock, reboot) move onto the new Agent API client. This retires two of the four `Home.razor` call sites from the two-session bug and guarantees the API is exercised by real traffic, not just HA.

### Step 6 — Deploy updates
- `Dockerfile.agent` / `deploy/docker/docker-compose.yml`: expose `8080` from the `netpilot-agent` service.
- `.env.example`: document token generation step.
- `docs/deployment.md`: new port, note LAN-only exposure.

### Step 7 — HA custom integration (`integrations/homeassistant/custom_components/netpilot/`)
Python, HACS-installable, config flow taking host + port + token. MVP entity set only:

| HA entity | Source |
|---|---|
| `device_tracker.netpilot_<device>` | device online state — presence detection, likely the single most useful thing this phase delivers |
| `switch.netpilot_limit_<device>` | per-device speed-limit override on/off |
| `switch.netpilot_block_<device>` | block/unblock |
| `binary_sensor.netpilot_router_online` | `system/info` |
| `sensor.netpilot_devices_online` | device list count |
| `button.netpilot_reboot_router` | reboot endpoint |

Custom services: `netpilot.set_device_limit`, `netpilot.block_device`, `netpilot.apply_policy`. No wireless `switch.*` entities yet — added when wireless lands.

### Step 8 — Tests
- `WebApplicationFactory`-based contract tests for the new endpoints: auth rejection, idempotent re-assert, capability gating, MAC canonical form.
- `RouterSessionManagerTests` — serialized access, re-login on expiry.
- Manual end-to-end: deploy to CT 109, install the custom_component against the user's real HA instance (this sandbox cannot reach either).

---

## 3. Explicitly deferred to a later phase

- Wireless entities (`switch.netpilot_wifi_*`) — needs Phase 3 steps 6–9 (wireless storage + reconciliation) first; tracked in `phase3-wireless-management-plan.md`.
- `GET /api/v1/events` (SSE push) — polling every 30s is adequate for the MVP entity set; build later without an API break.
- Usage-history sensors (`sensor.netpilot_<device>_usage`) — usage tracking exists in `NetPilot.Data`/`NetPilot.Core/Usage` already per phase2 docs; adding the HA sensor is cheap once the base API pattern is proven, deferred here only to keep this phase's surface small.
- HACS default-list submission / separate repo — same-repo for now, per the existing doc's recommendation.

---

## 4. Open items to confirm before implementation starts

1. Token generation UX — a dashboard button (new "API Access" panel/tab) vs. a one-time CLI/env-seeded token. Dashboard button matches the existing "settings editor doubles as the panel" pattern used for router connection.
2. Whether `/api/v1/devices/{mac}/block` and `/unblock` need any additional confirmation semantics (the dashboard likely already has a confirm-dialog pattern for reboot — mirror it, or is block/unblock considered low-risk enough to skip?).
