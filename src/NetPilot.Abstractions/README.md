# NetPilot.Abstractions

The router-agnostic contract. Zero dependencies — the smallest, most stable project in the solution. Everything else depends on it; it depends on nothing.

## Purpose

`NetPilot.Abstractions` defines `IRouterProvider`: the one seam every router brand implements. `NetPilot.Core` depends only on this interface, never on a concrete router SDK, so adding a new router brand is additive (a new provider project) rather than a change to core domain logic.

## Key types

| Type | Role |
|---|---|
| `IRouterProvider` | Connect, list devices, set a device's speed limit, read router identity. The full contract a provider must implement. |
| `RouterCapabilities` | Feature flags a provider declares (`SupportsSpeedLimit`, `SupportsDeviceCategorization`, `SupportsPriorityQos`, `SupportsGuestNetworkInfo`, `SupportsUsageTracking`) so `NetPilot.Core` and the dashboard can adapt to routers with less functionality than the AX53. |
| `RouterDeviceSnapshot` | One device's state as reported by a provider on a single poll — MAC, IP, hostname, raw category string, connection info, current limit state, usage snapshot. |
| `SpeedLimit` / `SpeedLimitState` | Desired vs. as-reported speed limit, in Kbps. `SpeedLimit.Unlimited` is the canonical "no limit" value. |
| `ConnectionInfo` / `ConnectionMedium` | Wired / 2.4GHz / 5GHz / Guest / Unknown, plus online/offline. |
| `UsageSnapshot` | Cumulative usage-counter reading (`TotalBytes`) for a device on this poll; `null` when a provider doesn't support usage tracking or couldn't parse the reading. |
| `RouterConnectionSettings` | Host, HTTPS flag, username, **already-decrypted** password — providers never see the encrypted form; `NetPilot.Data` owns encryption at rest. |
| `RouterInfo` | Model, firmware version, host — for the dashboard's connection-status panel. |

## Design notes

- All types are immutable `record`s — snapshots are compared by value, which is what makes the reconciliation loop's fingerprint check cheap and correct.
- `RawCategory` on `RouterDeviceSnapshot` is a plain string, not an enum — see [`NetPilot.Core`](../NetPilot.Core/README.md) for why `DeviceCategory` is data-driven.
- A provider that can't support a capability (e.g., no per-device usage counters) reports that honestly via `RouterCapabilities` rather than faking a value — `NetPilot.Core` and the dashboard degrade gracefully instead of assuming AX53-level functionality everywhere.

## Consumers

- [`NetPilot.Core`](../NetPilot.Core/README.md) — depends on this and nothing else for router access.
- [`NetPilot.Providers.TpLink`](../NetPilot.Providers.TpLink/README.md) — implements `IRouterProvider` against `TpLink.Sdk`.
- [`NetPilot.Agent`](../NetPilot.Agent/README.md) and [`NetPilot.Web`](../NetPilot.Web/README.md) — consume the registered `IRouterProvider` via DI.
