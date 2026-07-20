<div align="center">

# NetPilot

**AI-first, open-source home network automation platform.**

Automatically apply per-device speed limits based on device category — Mobile, Television, IP Camera, Game Console, and more — starting against a TP-Link Archer AX53, architected from day one to support other router brands.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Persistence](https://img.shields.io/badge/persistence-LiteDB-orange)](https://www.litedb.org/)
[![UI](https://img.shields.io/badge/UI-Blazor%20Server-5C2D91?logo=blazor&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Deploy](https://img.shields.io/badge/deploy-Docker%20Compose-2496ED?logo=docker&logoColor=white)](docs/deployment.md)
[![Tests](https://img.shields.io/badge/tests-xUnit-5D5D5D)](test)
[![License](https://img.shields.io/badge/license-TBD-lightgrey)](#)

</div>

---

## Table of contents

- [What it does](#what-it-does)
- [Architecture](#architecture)
- [Repository layout](#repository-layout)
- [Projects](#projects)
- [Technology](#technology)
- [Getting started](#getting-started)
- [Documentation](#documentation)
- [Security note](#security-note)

## What it does

NetPilot runs a background reconciliation loop against your router:

1. **Read** every connected device in one call.
2. **Resolve** the desired speed limit for each — its category's policy, or a per-device override.
3. **Compare** against a fingerprint of the limit last applied.
4. **Write** only the devices that drifted — no redundant router API calls on ticks where nothing changed.
5. **Track** offline/online transitions and per-device usage history.

A companion Blazor dashboard lets you review connected devices, edit category policies, set per-device overrides, watch a live activity log, and inspect usage history — all backed by the same embedded database the Agent writes to.

## Architecture

Two deployables sharing one embedded database, built on five supporting libraries:

```
                     TP-Link Archer AX53  (HTTP, LAN-only)
                              │
                        TpLink.Sdk               standalone, protocol-only client
                              │ implements
                  NetPilot.Providers.TpLink       thin adapter → IRouterProvider
                              │
                  IRouterProvider (NetPilot.Abstractions)
                  ← the one seam every router brand implements
                              │
                       NetPilot.Core              domain + application logic
              (Devices · Policy · Enforcement · Usage · Provider registry)
                              │
                       NetPilot.Data               LiteDB — embedded, no server
                    ┌─────────┴─────────┐
              NetPilot.Agent       NetPilot.Web
           background worker    Blazor Server dashboard
          (reconciliation loop)   (same shared .db file)
```

**Design principles that shape every project boundary above:**

- **One seam, any router brand.** `IRouterProvider` (`NetPilot.Abstractions`) is the only thing `NetPilot.Core` depends on for router access — never a concrete SDK. Adding a new brand means writing its protocol client plus one thin adapter project; core logic doesn't change.
- **The protocol client stands alone.** `TpLink.Sdk` has zero NetPilot dependencies and is independently publishable — built against protocol behavior confirmed live on a real AX53 (see [`docs/phase1-live-findings.md`](docs/phase1-live-findings.md)), not vendor documentation guesses.
- **Two deployables, one database.** `NetPilot.Agent` and `NetPilot.Web` are separate processes sharing one LiteDB file — restarting the dashboard never interrupts the reconciliation loop.
- **Categories are data.** `DeviceCategory` is string-keyed, not a fixed enum — seeded with categories confirmed live against a real router, but any new category a provider reports is auto-created and logged rather than dropped.

Full design rationale, domain model, and reconciliation-loop details live in [`docs/mvp-product-architecture.md`](docs/mvp-product-architecture.md).

## Repository layout

```
NetPilot/
├── src/
│   ├── NetPilot.Abstractions/      router-agnostic contract (IRouterProvider)
│   ├── NetPilot.Core/              domain model + policy/enforcement/usage logic
│   ├── NetPilot.Data/              LiteDB persistence, encrypted credentials
│   ├── TpLink.Sdk/                 standalone TP-Link protocol client
│   ├── NetPilot.Providers.TpLink/  TpLink.Sdk → IRouterProvider adapter
│   ├── NetPilot.Agent/             background reconciliation worker
│   └── NetPilot.Web/               Blazor Server dashboard
├── test/                           xUnit test projects, one per library
├── deploy/docker/                  Dockerfiles + docker-compose.yml
└── docs/                           architecture spec, live protocol findings, deployment guide
```

## Projects

| Project | Purpose |
|---|---|
| [`NetPilot.Abstractions`](src/NetPilot.Abstractions/README.md) | Router-agnostic contract (`IRouterProvider` and friends). Zero dependencies. |
| [`NetPilot.Core`](src/NetPilot.Core/README.md) | Domain model, policy reconciliation, usage tracking — router-agnostic application logic. |
| [`NetPilot.Data`](src/NetPilot.Data/README.md) | LiteDB persistence and encrypted-at-rest router credentials. |
| [`TpLink.Sdk`](src/TpLink.Sdk/README.md) | Standalone TP-Link Archer protocol client (auth, transport, device/usage parsing). |
| [`NetPilot.Providers.TpLink`](src/NetPilot.Providers.TpLink/README.md) | Thin adapter: `TpLink.Sdk` → `IRouterProvider`. |
| [`NetPilot.Agent`](src/NetPilot.Agent/README.md) | Background worker running the reconciliation loop. |
| [`NetPilot.Web`](src/NetPilot.Web/README.md) | Blazor Server dashboard: devices, policies, activity log, usage. |

## Technology

| Layer | Choice | Why |
|---|---|---|
| Runtime | .NET 10, C#, nullable enabled | current LTS-track SDK, used across every project |
| Persistence | LiteDB | embedded, no server, no migrations — one file, simple Docker deploys |
| Dashboard | Blazor Server (Interactive Server render mode) | real-time UI without hand-rolled polling/SignalR, no separate frontend toolchain |
| Secrets at rest | ASP.NET Core Data Protection | encrypts the router password before it touches LiteDB; key ring shared between Agent and Web |
| Router protocol | Hand-built `TpLink.Sdk` (RSA login handshake, plain-JSON `smart_network` endpoints) | confirmed live against a real Archer AX53 — see [`docs/phase1-live-findings.md`](docs/phase1-live-findings.md) |
| Tests | xUnit | `test/NetPilot.Core.Tests`, `test/NetPilot.Data.Tests`, `test/TpLink.Sdk.Tests` |
| Deployment | Docker Compose, two images, one shared named volume | `Dockerfile.agent` + `Dockerfile.web`; see [`docs/deployment.md`](docs/deployment.md) |

## Getting started

```bash
git clone <this-repo>
cd NetPilot
dotnet build
dotnet test
```

To run against a real router without Docker:

1. Set `NetPilot:DataDirectory` (defaults to `../../data`) for both `NetPilot.Agent` and `NetPilot.Web`.
2. Run both processes — either configure the router connection from the Web dashboard once it's up, or set `ROUTER_HOST` / `ROUTER_PASSWORD` environment variables to seed the connection on first run.

For a full Docker Compose deployment — the recommended way to actually run this on your network — see [`docs/deployment.md`](docs/deployment.md).

## Documentation

| Doc | Contents |
|---|---|
| [`docs/mvp-product-architecture.md`](docs/mvp-product-architecture.md) | Architecture spec: project structure, domain model, provider abstraction, deployment shape |
| [`docs/phase1-live-findings.md`](docs/phase1-live-findings.md) | Confirmed-live TP-Link protocol details — login flow, endpoints, field names, units |
| [`docs/deployment.md`](docs/deployment.md) | How to deploy with Docker Compose |
| `docs/phase2-*.md` | Usage-tracking feature planning |
| `docs/NetPilot_Research_Findings_and_Architecture.md` | Earlier, superseded research (background only) |

## Security note

The dashboard assumes LAN-only reachability — there is no built-in authentication in v1. Do not expose `NetPilot.Web`'s port directly to the internet.
