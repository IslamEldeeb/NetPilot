# NetPilot.Web

Blazor Server dashboard. The other of two deployables sharing the shared LiteDB file — never merged with [`NetPilot.Agent`](../NetPilot.Agent/README.md), so restarting the dashboard (e.g. after a UI update) never interrupts the reconciliation loop.

## Purpose

A single-page (`Components/Pages/Home.razor`) real-time dashboard, rendered with Blazor's Interactive Server mode — server-pushed UI updates over a persistent connection, no hand-rolled polling/SignalR code, no separate frontend toolchain.

**Why Blazor Server specifically:** the underlying state (device list, usage) changes on its own every reconciliation tick, independent of user action — Interactive Server render mode handles that naturally. Trade-off: one persistent connection per open browser tab, fine for a single-user home dashboard.

## Panels (tabs)

| Tab | Contents |
|---|---|
| **Router connection** (top of page, not a tab) | Provider, host, HTTPS/username, connected/issue status. Doubles as the settings editor — saving here writes straight through `IRouterConnectionStore`/`RouterPasswordProtector`, same record `NetPilot.Agent` reads. Includes a "Test connection" action and a manual "Refresh from router" trigger. |
| **Devices** | Grouped by category, with online-only / category / free-text search filters. Each row shows medium (wired/2.4GHz/5GHz/guest), online status, effective limit, and — via `RouterLimitDrift` — whether the router's actually-reported limit currently matches what's expected. |
| **Device type policies** | One row per `DeviceCategory` seen so far; editing a row bumps `DevicePolicy.DefinitionVersion`, which invalidates every device fingerprint in that category at once — no separate "push" step. Includes a bulk "apply to all categories" control. |
| **Activity log** | Recent `ActivityLogEntry` rows, newest first — the same log `PolicyReconciliationService` and `UsageTrackingService` write to; doubles as a debugging tool. |
| **Usage** | Per-device usage totals by month or day, backed by `UsageQuery.BytesByDevice` combining live running state with historical `UsageHistoryEntry`/`UsageDailyHistoryEntry` rows. Filterable by device; all timestamps shown in UTC. |

## Wiring

`Program.cs` calls the same `AddNetPilotData` / `AddTpLinkProvider` registration `NetPilot.Agent` uses, pointed at the same `NetPilot:DataDirectory`, plus `AddRazorComponents().AddInteractiveServerComponents()`. `Home.razor` injects the store interfaces (`IDeviceStore`, `IPolicyStore`, `IActivityLogStore`, `IUsageStore`, `IRouterConnectionStore`), `RouterPasswordProtector`, the active `IRouterProvider`, and both `NetPilot.Core` services directly — it reads/writes the same data the Agent's reconciliation loop does, live.

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `NetPilot:DataDirectory` | `../../data` (Docker: `/data`) | Must point at the same shared volume as `NetPilot.Agent` |
| `ASPNETCORE_URLS` | (Docker: `http://+:8080`) | Listen address |

## Running

```bash
dotnet run --project src/NetPilot.Web
```

For Docker deployment, see [`docs/deployment.md`](../../docs/deployment.md) — built from `deploy/docker/Dockerfile.web`, published port `8080`.

## Security note

There is no built-in authentication — this assumes LAN-only reachability. Do not expose this port directly to the internet.
