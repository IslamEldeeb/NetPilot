# NetPilot MVP — Product Architecture Proposal

**Status:** Proposal only. No production code written against this yet — awaiting your approval per your instructions.
**Framing shift:** router support (starting with TP-Link) is now infrastructure in service of the product, not the product itself — and per your latest direction, it's built from the start as *one of potentially several* router integrations, not a TP-Link-only system with a theoretical escape hatch.
**Revision note:** this replaces the previous version. Three things changed based on your last few messages, folded in below: (1) `DeviceCategory` is now dynamic/data-driven, not a fixed enum (resolves the open question from our category discussion); (2) persistence is LiteDB, not EF Core/SQLite (resolves the open question from our Redis discussion — same reasoning: embedded, no server, and now also no migration step to break in a Docker container); (3) router support is a proper provider/plugin architecture, and there's a full deployment section for Docker on your Proxmox box.

---

## 1. Overall architecture

Five runtime pieces, one shared embedded database, one job to do well: *keep every device's speed limit matching its category's policy, without re-sending limits that are already correct — regardless of which brand of router is actually doing the enforcing.*

```
┌───────────────────────┐        ┌───────────────────────┐
│  TP-Link Archer AX53   │        │  (future) other brands │
│  (confirmed protocol)  │        │  Netgear / OpenWrt / …  │
└───────────┬────────────┘        └───────────┬────────────┘
            │ HTTP                             │ whatever that brand needs
┌───────────▼────────────┐        ┌────────────▼───────────┐
│      TpLink.Sdk          │        │   (future) FooBrand.Sdk  │
│  raw protocol client,    │        │   same idea, own project  │
│  standalone-publishable   │        │                           │
└───────────┬────────────┘        └────────────┬───────────┘
            │ implements                        │ implements
┌───────────▼────────────┐        ┌────────────▼───────────┐
│ NetPilot.Providers.TpLink│        │ (future) …Providers.Foo  │
│  thin adapter — translates│       │  same shape, copy-paste   │
│  TpLink.Sdk → IRouterProvider│    │  starting point           │
└───────────┬─────────────┘        └────────────┬───────────┘
            │                                    │
            └─────────────────┬──────────────────┘
                    IRouterProvider (NetPilot.Abstractions)
                    ← the ONE seam every router brand implements
                               │
┌──────────────────────────────▼──────────────────────────────┐
│                          NetPilot.Core                         │  domain + application logic
│   Devices/   Policy/   Enforcement/   Providers/               │  100% router-agnostic
│   DeviceCategory (dynamic), DevicePolicy,                       │
│   PolicyReconciliationService, RouterProviderRegistry            │
└──────────────────────┬─────────────────────────┬───────────────┘
                        │ IDeviceStore etc.       │
              ┌─────────▼─────────┐       ┌───────▼──────────┐
              │   NetPilot.Data     │       │                  │
              │   LiteDB (embedded) │       │                  │
              └─────────┬───────────┘       │                  │
                        │ one shared .db file │                  │
              ┌─────────▼─────────┐   ┌──────▼────────────────┐
              │   NetPilot.Agent    │   │    NetPilot.Web         │
              │  background worker  │   │  Blazor Server           │
              │  reconciliation loop│   │  dashboard               │
              └────────────────────┘   └─────────────────────────┘
```

**What changed structurally vs. the first pass:** one new layer — `NetPilot.Abstractions` (the contract) plus a thin per-brand `NetPilot.Providers.*` adapter — sits between `NetPilot.Core` and any specific router SDK. `TpLink.Sdk` itself is untouched by this; it stays a standalone, protocol-only, independently publishable client (someone could `dotnet add package TpLink.Sdk` for a totally unrelated project). The adapter project is where "this is a NetPilot router integration" lives, and it's intentionally thin — translating one SDK's models into NetPilot's shared shape, nothing else. Adding brand #2 later means writing (or finding) that brand's protocol client, then writing one small adapter that looks a lot like `NetPilot.Providers.TpLink` — `NetPilot.Core` never changes.

**Why this is worth the extra project now, not later:** retrofitting a multi-brand abstraction onto code that assumed "the router" was always TP-Link is real rework — every place that quietly leaked a TP-Link-specific assumption (a MAC format, a Kbps-vs-Mbps unit, an enable flag being a string `"on"` instead of a bool) has to be found and fixed. Designing the seam first costs one interface and one small adapter project up front and means every future brand is additive, not a refactor.

## 2. Project structure

```
NetPilot/
  src/
    NetPilot.Abstractions/        NEW — the router-agnostic contract. Zero dependencies on
                                    any specific router SDK. This is the smallest, most
                                    stable project in the solution — everything else depends
                                    on it, it depends on nothing.
      IRouterProvider.cs
      RouterCapabilities.cs
      RouterDeviceSnapshot.cs
      SpeedLimit.cs
      RouterConnectionSettings.cs

    TpLink.Sdk/                   existing, unchanged — raw TP-Link protocol client
                                    (Phase 1, confirmed live against your AX53)

    NetPilot.Providers.TpLink/    NEW — thin adapter implementing IRouterProvider by
                                    wrapping TpLink.Sdk. This is the template every future
                                    brand's provider copies.

    (future, structurally reserved — not built until there's a second real router to test against):
    NetPilot.Providers.Netgear/
    NetPilot.Providers.OpenWrt/    probably the highest-value one long term — many
                                    third-party firmwares (OpenWrt/DD-WRT/OPNsense) expose a
                                    more standardized API surface than any single OEM brand

    NetPilot.Core/                 domain + application logic, 100% router-agnostic
      Devices/                      Device, DeviceCategory (dynamic — see §4)
      Policy/                       DevicePolicy, per-device Override
      Enforcement/                  PolicyReconciliationService — the "brain," depends only
                                     on IRouterProvider, never on a concrete SDK
      Providers/                    RouterProviderRegistry — holds/selects the active
                                     provider (see §3 for how this scales to real plugins)

    NetPilot.Data/                 LiteDB-backed persistence — Devices, Policies,
                                     ActivityLog, RouterConnection, all as LiteDB collections

    NetPilot.Agent/                existing — BackgroundService running the reconciliation loop
    NetPilot.Web/                  NEW — Blazor Server dashboard

  test/
    NetPilot.Core.Tests/           policy/reconciliation logic against a fake IRouterProvider
                                     — no router, no DB, no Docker needed to run these
    TpLink.Sdk.Tests/               replays the JSON fixtures captured in phase1-live-findings.md

  deploy/
    docker/
      Dockerfile.agent
      Dockerfile.web
      docker-compose.yml            both services + one shared named volume
      .env.example                  ROUTER_HOST=, ROUTER_PASSWORD= (first-run seed only)

  docs/                            existing research/architecture docs
```

Target framework: **.NET 10** across every project (matches what's already scaffolded).

## 3. Router provider architecture — how "support any router type" actually works

This is the part that makes the open-source, multi-brand story real rather than aspirational.

```csharp
namespace NetPilot.Abstractions;

public interface IRouterProvider
{
    string ProviderId { get; }        // e.g. "tplink-archer-ax-series"
    string DisplayName { get; }       // e.g. "TP-Link Archer (AX-series)"
    RouterCapabilities Capabilities { get; }

    Task ConnectAsync(RouterConnectionSettings settings, CancellationToken ct);
    Task<IReadOnlyList<RouterDeviceSnapshot>> GetDevicesAsync(CancellationToken ct);
    Task SetSpeedLimitAsync(string macAddress, SpeedLimit limit, CancellationToken ct);
    Task<RouterInfo> GetRouterInfoAsync(CancellationToken ct); // model/firmware, for the status panel
}

public record RouterCapabilities(
    bool SupportsSpeedLimit,
    bool SupportsDeviceCategorization,   // not every brand will fingerprint device types
    bool SupportsPriorityQos,
    bool SupportsGuestNetworkInfo);

public record RouterDeviceSnapshot(
    string MacAddress, string IpAddress, string Hostname,
    string? RawCategory,      // whatever the router calls it natively; null if unsupported
    ConnectionInfo Connection,
    SpeedLimitState CurrentLimit);
```

**Capability negotiation matters because not every router will be as good as this one.** The AX53 happens to hand us human-readable device categories for free (confirmed live — likely Fing-backed, per the login-flow capture). A cheaper or older router might support enable/disable-only limits with no fine-grained Kbps control, or no device typing at all. `RouterCapabilities` lets `NetPilot.Core` and the dashboard adapt honestly — grey out "Priority" if the connected provider doesn't support it, fall back to `NetPilot.Core`'s own OUI/hostname classifier (from the original Phase 1 SDK research — worth keeping for exactly this reason, not discarding just because TP-Link doesn't need it) when `SupportsDeviceCategorization` is false.

**How providers get registered — deliberately staged, not over-built for v1:**

- **Now:** providers are ordinary compile-time project references, registered via a plain DI extension method (`services.AddTpLinkProvider(config)`). Zero dynamic-loading machinery. This is enough for "NetPilot supports TP-Link, architected so it isn't only TP-Link."
- **Later, once there's real demand from a second/third contributed provider:** upgrade to true drop-in plugin loading — scan a `providers/` folder for DLLs implementing `IRouterProvider`, load each into its own `AssemblyLoadContext` (the standard .NET mechanism for isolated, unloadable plugins), register into DI automatically. This is a mechanical addition at that point (one `PluginLoader` class), *not* a redesign — specifically because the contract (`IRouterProvider`) will already be proven correct by having 2+ real implementations behind it. Building the dynamic-loading machinery *before* a second provider exists risks designing a plugin API for a shape of problem you're guessing at rather than one you've actually seen twice.

## 4. Domain model

```csharp
// DeviceCategory is data, not a fixed enum — see rationale below.
record DeviceCategory(string Key, string DisplayName); // e.g. ("mobile", "Mobile")

record SpeedLimit(bool Enabled, int? DownloadKbps, int? UploadKbps); // null = unlimited

record DevicePolicy(string CategoryKey, SpeedLimit Limit, int DefinitionVersion);

class Device // aggregate root, identity = MAC address (stable across router restarts, DHCP changes)
{
    MacAddress Mac;
    string Hostname;
    string? FriendlyName;              // optional user-facing rename
    string CategoryKey;                // last value reported by whichever provider is active
    ConnectionState Connection;        // wired/2.4G/5G/guest + online/offline
    SpeedLimit? Override;              // per-device override — wins over category policy if set
    string? LastAppliedFingerprint;    // hash of the last SpeedLimit actually written to the router
    DateTimeOffset FirstSeenAtUtc;
    DateTimeOffset LastSeenAtUtc;
}

record ActivityLogEntry(DateTimeOffset AtUtc, ActivityEventType Type, MacAddress Mac, string Message);

enum ActivityEventType
{
    DeviceDiscovered, DeviceWentOffline, DeviceCameBackOnline,
    PolicyApplied, PolicySkippedAlreadyCorrect, WriteFailed,
    NewCategorySeen  // a provider reported a category NetPilot has never seen before
}
```

**Why `DeviceCategory` is a string-backed record now, not the fixed 14-value enum from the first draft:** we only sampled 26 devices on one network at one moment — the AX53's real vocabulary is almost certainly larger, and a second router brand later might use an entirely different set of category names (or none at all). A hard-coded enum bakes in "we've seen everything there is to see," which isn't true and becomes actively wrong the moment a router (this one or a future brand) reports something new. Instead: **seed** the policy table on first run with the 13 categories already confirmed live (sensible starting defaults matching your sketch), but treat any never-seen `RawCategory` from a provider as valid — auto-create a policy row for it with a safe fallback limit, log `NewCategorySeen` so it's visible, and let you assign it a real policy whenever you notice. Same mechanism whether the surprise category comes from a TP-Link firmware update or a totally different brand later.

`LastAppliedFingerprint` is the whole answer to "remember it's already applied, don't repeat unnecessary updates" — a cheap hash of `(CategoryKey, Override, PolicyDefinitionVersion)`. If it matches what the device's policy would compute *right now*, skip — no API call. It only changes when: the device is new, its category changes, its override changes, or you edit that category's policy. That's a strictly smaller set of triggers than "every poll cycle," which is the whole efficiency ask.

## 5. Router connection configuration

**What needs storing:** host/IP, whether to use HTTPS (confirmed yes for the AX53, self-signed cert), and the password. **Not a username** for this router specifically — the confirmed live login flow takes only `operation=login&password=<encrypted>`, no separate username field. `RouterConnectionSettings` keeps an optional `Username` (default `"admin"`) purely for forward-compatibility with other TP-Link firmwares/models or future brands that might need one — not required here.

**Where it lives:** a `RouterConnection` LiteDB document — for v1, a single record (`ProviderId`, `Host`, `UseHttps`, `Username`, `EncryptedPassword`). Both `NetPilot.Agent` and `NetPilot.Web` read the same source. Sets up multi-router later cleanly — same collection, one document per configured router instead of one.

**How the password is protected:** encrypted at rest via ASP.NET Core's Data Protection API before it touches the LiteDB file — never plaintext on disk. Both processes need to decrypt it, so they share one Data Protection key ring, persisted to a folder alongside the database file (in Docker: the same mounted volume as the DB — see §8).

**How it gets set:** the dashboard's "Router connection status" panel doubles as the settings editor. For Docker specifically, `.env` / compose environment variables (`ROUTER_HOST`, `ROUTER_PASSWORD`) can **seed** the LiteDB record on first container start if it's empty — convenient for a one-command `docker compose up` first run — but the dashboard remains the source of truth after that; editing there updates the same record.

**Certificate handling:** the AX53's HTTPS cert is self-signed. `TpLink.Sdk`'s `HttpClient` needs a certificate-validation exception scoped specifically to the configured host — not a blanket "accept all certs" setting.

## 6. Application flow (the reconciliation loop)

What `NetPilot.Agent`'s `BackgroundService` does every tick (default 30s, configurable):

1. **One read.** `IRouterProvider.GetDevicesAsync()` — one HTTP call (confirmed live: `admin/smart_network?form=game_accelerator`, `operation=loadDevice`) returns every connected device's current state. No per-device polling.
2. **For each device:**
   - Take its `RawCategory` from the provider as `CategoryKey` directly (no enum mapping — see §4); if the provider reports `SupportsDeviceCategorization: false` or `RawCategory` is null, fall back to `NetPilot.Core`'s own OUI/hostname heuristic.
   - Look up the `Device` by MAC in `NetPilot.Data`. New MAC → newly discovered device → create, log `DeviceDiscovered`. New `CategoryKey` never seen before → auto-create its policy row, log `NewCategorySeen`.
   - Resolve desired `SpeedLimit`: device's `Override` if set, else the `DevicePolicy` for its category.
   - Compare fingerprints. **Match → skip, no API call.** **Mismatch →** call `IRouterProvider.SetSpeedLimitAsync(mac, limit)` (confirmed live: `admin/smart_network?form=client_speed_limit`, `operation=write`); on success update the fingerprint and log `PolicyApplied`; on failure log `WriteFailed` and retry next tick.
3. **Devices no longer reported** → mark offline, log `DeviceWentOffline` once. Row (category, override, fingerprint) survives so nothing needs re-applying on reconnect.

Implements all five steps you specified, in order, with the fingerprint check doing step 5's work.

## 7. Dashboard (NetPilot.Web)

Four panels, unchanged from your spec:

- **Router connection status** — provider in use, host, last successful poll, firmware/model, reachable/unreachable. Doubles as the settings editor (§5).
- **Connected devices** — grouped by category, current real-time rate, configured limit, online/offline.
- **Device type policies** — your sketch, rendered from whatever categories actually exist (seeded + auto-discovered per §4):

  | Category | Download | Upload |
  |---|---|---|
  | Mobile | 5 Mbps | 1 Mbps |
  | Television | Unlimited | Unlimited |
  | IP Camera | 2 Mbps | — |
  | Unknown Device | 5 Mbps | — |

  Editing a row bumps `DefinitionVersion`, invalidating every device fingerprint in that category — one edit, applied everywhere automatically, no separate "push."
- **Per-device overrides** — optional, same fingerprint mechanism.
- **Activity log** — recent `ActivityLogEntry` rows, newest first; doubles as your debugging tool.

**Why Blazor Server:** real-time state that changes on its own (every 30s, independent of user action) without hand-rolled polling/SignalR code; stays pure C#, no separate frontend toolchain. Trade-off: persistent connection per open tab — fine for a single-user home dashboard, would need revisiting if this ever became multi-tenant hosted software. Not a v1 concern.

## 8. Deployment — Docker on your Proxmox home server

**Unit of deployment:** `docker compose`. This is Proxmox-agnostic by design — it runs identically whether the Docker host is a Proxmox VM, a privileged LXC with Docker installed, or bare metal elsewhere. Proxmox is just the hypervisor underneath; nothing here is Proxmox-specific, which is exactly what makes it portable for other people self-hosting this open source later too.

**Two images, one compose file:**

```
deploy/docker/
  Dockerfile.agent        multi-stage: dotnet publish → mcr.microsoft.com/dotnet/runtime:10.0
  Dockerfile.web          multi-stage: dotnet publish → mcr.microsoft.com/dotnet/aspnet:10.0
  docker-compose.yml
  .env.example
```

```yaml
# docker-compose.yml (shape, not final)
services:
  netpilot-agent:
    build: { context: ../.., dockerfile: deploy/docker/Dockerfile.agent }
    volumes: [ netpilot-data:/data ]
    environment:
      - ROUTER_HOST=${ROUTER_HOST}
      - ROUTER_PASSWORD=${ROUTER_PASSWORD}
    restart: unless-stopped

  netpilot-web:
    build: { context: ../.., dockerfile: deploy/docker/Dockerfile.web }
    volumes: [ netpilot-data:/data ]
    ports: [ "8080:8080" ]
    restart: unless-stopped

volumes:
  netpilot-data:   # LiteDB file + Data Protection keyring, shared by both containers
```

**Why two images instead of one combined container:** preserves the "monitoring must not die because the dashboard restarted" reasoning from §1 all the way into production, not just in local dev. `docker compose restart netpilot-web` (e.g., after a dashboard update) never interrupts the reconciliation loop.

**Networking:** no special Docker network mode needed. The containers only make *outbound* HTTP calls to the router (`192.168.1.1`) — default Docker bridge networking already routes outbound traffic to other LAN devices through the host, the same way any container reaches any external site. `host` or `macvlan` networking would only matter if something needed to discover or be discovered *from* the LAN, which nothing here does.

**Why LiteDB fits this deployment specifically, beyond the earlier "small scope" reasoning:** no migration step to run (or forget to run) on container start — the file is just created on first write. Backup is `docker cp` or a volume snapshot of one file. One less thing to get wrong in a fresh Proxmox deploy.

**Open-source angle this unlocks for free:** once these two Dockerfiles exist, publishing built images to GitHub Container Registry (`ghcr.io/<you>/netpilot-agent`, `ghcr.io/<you>/netpilot-web`) via a CI workflow means anyone else can self-host NetPilot with the same `docker-compose.yml` and zero .NET toolchain of their own — meaningfully lowers the bar for other people actually trying an open-source home-network tool.

## 9. MVP scope

**In:** the five-step flow, four dashboard panels, LiteDB persistence, the `IRouterProvider` seam with TP-Link as the first (and for v1, only) implementation, Docker deployment.

**Explicitly out (deferred, not forgotten):**
- A second real router provider — the *seam* is built now; the *second implementation* waits for a real router to test against (yours, or a contributor's).
- Dynamic plugin loading (`AssemblyLoadContext`) — documented upgrade path (§3), not built until there's a second provider to prove the contract against.
- TMP/Tether protocol — still parked.
- Traffic-shaping bridge fallback — not needed, the router API works.
- Dashboard authentication/multi-user access — v1 assumes LAN-only reachability. Worth a one-line README callout so it isn't accidentally exposed to the internet.
- Notifications, AI-assisted categorization, usage-history charts — see §10, all natural adds on top of what's built here, none are dependencies of the MVP working.

## 10. Future expansion points

- **New router brand:** write its protocol client + one `NetPilot.Providers.*` adapter; `NetPilot.Core` doesn't change.
- **Real plugin loading:** `AssemblyLoadContext`-based discovery once 2+ providers exist (§3).
- **Notifications:** subscribe to `ActivityLogEntry` writes (e.g., `DeviceDiscovered`) — no change to the reconciliation loop.
- **AI-assisted categorization:** a fallback classifier that only kicks in when `RawCategory` is null or generic (`Unknown`/`Smart Device`) — doesn't touch the confirmed-good cases.
- **Historical usage charts:** the router already reports `trafficUsage`/`onlineTime` per device — `NetPilot.Data` can start recording snapshots any time.
- **Mobile access:** `NetPilot.Web` can grow a thin JSON API alongside Blazor pages later.
- **Multi-router/mesh:** `Device` is already keyed globally by MAC, not scoped to one router — adding a second *configured router* (not just brand) is a `RouterId` column and a dashboard filter, not a rearchitecture.

## 11. One practical constraint worth naming now

I don't have a `dotnet` toolchain in my sandbox — I can write the C# files, but not compile or run tests here as I go. For implementation, the loop will be: I write code + unit tests (`TpLink.Sdk.Tests` replays the live-captured fixtures, meaningful without a live router), and you (or your other Claude Code session, which does have `dotnet`) run `dotnet build`/`dotnet test` to catch what I get wrong syntactically, plus `docker compose build` to validate the container story end-to-end.

---

**Waiting on your approval before writing any code.** If this shape is right, next step is turning §3–§6 into actual C# across `NetPilot.Abstractions`, `NetPilot.Providers.TpLink`, `NetPilot.Core`, `NetPilot.Data`, plus the two new projects and the `deploy/docker` files.
