# NetPilot.Agent

Background worker (`Microsoft.NET.Sdk.Worker`) running the reconciliation loop. One of two deployables sharing the shared LiteDB file — never merged with [`NetPilot.Web`](../NetPilot.Web/README.md).

## Purpose

`Worker` is a `BackgroundService` that, on a timer, reads every device from the router, resolves the correct speed limit for each, and writes only what's actually wrong. It has no UI and exposes no ports — it exists purely to keep the router's actual state matching policy.

## What it does each tick

1. Ensures seed device categories exist (first run only).
2. Seeds the router connection from `ROUTER_HOST`/`ROUTER_PASSWORD` env vars if the stored connection is empty (no-op after the first successful seed — the dashboard is the source of truth from then on).
3. Connects to the router if not already connected.
4. Calls `PolicyReconciliationService.ReconcileAsync` (see [`NetPilot.Core`](../NetPilot.Core/README.md)) — one device read, per-device fingerprint compare, write only what drifted.
5. Feeds the same snapshot into `UsageTrackingService.TrackAsync` — no extra router call for usage data.
6. Sleeps for `NetPilot:PollIntervalSeconds` (default 180s) and repeats.

A failed tick (router offline, bad password, transient network error) is logged and never crashes the worker — it just reconnects and retries on the next tick.

## Configuration

| Key | Source | Default | Purpose |
|---|---|---|---|
| `NetPilot:DataDirectory` | `appsettings.json` / env `NetPilot__DataDirectory` | `../../data` (Docker: `/data`) | Where the shared LiteDB file and Data Protection key ring live |
| `NetPilot:PollIntervalSeconds` | `appsettings.json` | `180` | Reconciliation tick interval |
| `ROUTER_HOST` | Environment variable | — | First-run seed only; ignored once a connection record exists |
| `ROUTER_PASSWORD` | Environment variable | — | First-run seed only; encrypted before being stored |

## Dependencies

References [`NetPilot.Core`](../NetPilot.Core/README.md), [`NetPilot.Data`](../NetPilot.Data/README.md), [`NetPilot.Providers.TpLink`](../NetPilot.Providers.TpLink/README.md), and [`NetPilot.Abstractions`](../NetPilot.Abstractions/README.md). `Program.cs` wires `AddNetPilotData`, `AddTpLinkProvider`, `PolicyReconciliationService`, `UsageTrackingService`, and registers `Worker` as the hosted service.

## Running

```bash
dotnet run --project src/NetPilot.Agent
```

For Docker deployment (recommended for actually running this against your router), see [`docs/deployment.md`](../../docs/deployment.md) — built from `deploy/docker/Dockerfile.agent`.
