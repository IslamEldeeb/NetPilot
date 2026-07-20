# NetPilot.Core

Domain model and application logic. 100% router-agnostic — depends only on [`NetPilot.Abstractions`](../NetPilot.Abstractions/README.md), never on a concrete router SDK.

## Purpose

This is the "brain" of NetPilot: it decides what each device's speed limit *should* be and whether the router actually needs to be told about it. Everything here works against `IRouterProvider` and a set of storage interfaces (`IDeviceStore`, `IPolicyStore`, `IActivityLogStore`, `IUsageStore`, `IRouterConnectionStore`) — the concrete LiteDB implementations live in [`NetPilot.Data`](../NetPilot.Data/README.md).

## Folders

| Folder | Contents |
|---|---|
| `Devices/` | `Device` aggregate, `DeviceCategory`, `MacAddress`, `DeviceClassifier` fallback, `RouterLimitDrift` |
| `Policy/` | `DevicePolicy`, `PolicyFingerprint` |
| `Enforcement/` | `PolicyReconciliationService`, `ActivityLogEntry`, `ActivityEventType` |
| `Usage/` | `UsageTrackingService`, `DeviceUsageState`, usage history entries, `UsageQuery` |
| `Providers/` | `RouterProviderRegistry` |
| `RouterConnection/` | `RouterConnection`, `IRouterConnectionStore` |

## Core mechanics

### The reconciliation loop — `PolicyReconciliationService`

Called once per tick by [`NetPilot.Agent`](../NetPilot.Agent/README.md). Per device:

1. Resolve its category — `RawCategory` from the provider if `RouterCapabilities.SupportsDeviceCategorization` is true, otherwise `HeuristicDeviceClassifier` (hostname keyword matching — `iphone` → `mobile`, `xbox` → `game_console`, etc.).
2. Look it up by MAC; new MAC → `DeviceDiscovered`. Never-seen category → auto-create a fallback policy (`Unlimited`, `IsUserConfigured: false`) and log `NewCategorySeen`.
3. Resolve the desired limit: the device's `Override` if set, else its category's policy.
4. Compute a `PolicyFingerprint` (SHA-256 of category + override + policy version) and compare to `LastAppliedFingerprint`. Match → skip, log `PolicySkippedAlreadyCorrect`, **no router write**.
5. Mismatch → `IRouterProvider.SetSpeedLimitAsync`; success updates the fingerprint and logs `PolicyApplied`; failure logs `WriteFailed` and retries next tick.
6. Devices no longer reported by the router are marked offline once (`DeviceWentOffline`) — their row survives so nothing needs re-applying on reconnect.

**Safety rule worth knowing:** a policy only gets pushed to the router if a human actually saved it via the dashboard (`DevicePolicy.IsUserConfigured`) or the device has an explicit override. Auto-created fallback policies for never-seen categories are `Unlimited` but *not user-configured*, so a fresh/empty local database can never silently push `Unlimited` over limits someone already configured through a different deployment against the same router.

### DeviceCategory is data, not an enum

Seeded with the 13 categories confirmed live against a real AX53 (`laptop`, `mobile`, `television`, `ip_camera`, `game_console`, …), but `DeviceCategory.KeyFrom` turns any router-reported category string into a stable key — a category NetPilot has never seen is auto-created rather than dropped.

### Usage tracking — `UsageTrackingService`

Runs off the same snapshot the reconciliation service already fetched (no extra router call). Turns the router's raw, reset-prone cumulative usage counter into durable daily and monthly totals:

- Counter increases → the delta is added to the running day/month total.
- Counter decreases from the last observed value → treated as a reset (cause-agnostic — restart, daily rollover, whatever) and logged as `UsageCounterReset`; accumulation resumes from the new baseline.
- A device's very first-ever reading is counted in full immediately rather than held back as a silent baseline.
- Day/month rollovers flush the closed period into `UsageHistoryEntry` / `UsageDailyHistoryEntry` before resetting the running counter.

### RouterProviderRegistry

Holds whatever `IRouterProvider`s are registered via DI. v1 assumes exactly one configured router (`GetActive()` throws if zero or more than one is registered) — multi-router support is a documented future step, not built yet.

## Consumers

- [`NetPilot.Agent`](../NetPilot.Agent/README.md) — runs `PolicyReconciliationService` and `UsageTrackingService` every tick.
- [`NetPilot.Web`](../NetPilot.Web/README.md) — reads/writes the same store interfaces directly for the dashboard.
- [`NetPilot.Data`](../NetPilot.Data/README.md) — implements every store interface this project defines.

## Tests

[`test/NetPilot.Core.Tests`](../../test/NetPilot.Core.Tests) exercises policy/reconciliation logic against a fake `IRouterProvider` and in-memory stores — no real router, database, or Docker needed.
