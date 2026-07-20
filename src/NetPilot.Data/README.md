# NetPilot.Data

LiteDB-backed persistence. Implements every store interface [`NetPilot.Core`](../NetPilot.Core/README.md) defines, plus encryption for the router password at rest.

## Purpose

One embedded LiteDB file holds every NetPilot document — devices, policies, activity log, usage history, and the router connection. No server process, no migrations: the file is created on first write, and backup is just a copy of one file. `NetPilot.Agent` and `NetPilot.Web` each open their own handle against the *same* file on a shared volume.

## Key types

| Type | Role |
|---|---|
| `NetPilotDatabase` | Owns the shared LiteDB file. Opens it with `Connection=shared` specifically so writes from one process (e.g. Agent) become visible to the other (e.g. Web) — LiteDB's default connection mode caches the file per-process, which would break cross-process visibility. |
| `RouterPasswordProtector` | Wraps `Microsoft.AspNetCore.DataProtection` to encrypt/decrypt the router password before it touches LiteDB. Agent and Web share one key ring (see below) so either process can decrypt what the other encrypted. |
| `Lite*Store` classes | `LiteDeviceStore`, `LitePolicyStore`, `LiteActivityLogStore`, `LiteRouterConnectionStore`, `LiteUsageStore` — one per `NetPilot.Core` store interface, each mapping a domain type to/from its own `*Document` shape. |
| `Documents/` | Plain LiteDB document classes (`DeviceDocument`, `PolicyDocument`, `ActivityLogDocument`, `RouterConnectionDocument`, `UsageStateDocument`, `UsageHistoryDocument`, `UsageDailyHistoryDocument`) — kept separate from domain types so storage shape can evolve independently of the domain model. |
| `ServiceCollectionExtensions.AddNetPilotData` | Single DI registration entry point: wires the database, Data Protection key ring, every store, and the password protector. |

## Wiring it up

Both `NetPilot.Agent` and `NetPilot.Web` call the same extension method with matching paths on the shared volume:

```csharp
services.AddNetPilotData(dbPath: "/data/netpilot.db", keyRingPath: "/data/keys");
```

This registers:

- `NetPilotDatabase` (singleton, one shared LiteDB file)
- `IDeviceStore`, `IPolicyStore`, `IActivityLogStore`, `IRouterConnectionStore`, `IUsageStore` → their `Lite*Store` implementations
- `IDeviceClassifier` → `HeuristicDeviceClassifier` (from `NetPilot.Core`)
- ASP.NET Core Data Protection, persisted to `keyRingPath`, application name `"NetPilot"`
- `RouterPasswordProtector`

## Security

The router password is never stored in plaintext. `RouterPasswordProtector` encrypts it with a purpose-scoped `IDataProtector` (`"NetPilot.RouterConnection.Password"`) before `LiteRouterConnectionStore` writes it to LiteDB. Both processes must point at the same `keyRingPath` — if the key ring is lost, previously encrypted passwords can no longer be decrypted and the connection must be re-entered.

## Consumers

- [`NetPilot.Agent`](../NetPilot.Agent/README.md) — calls `AddNetPilotData` on startup.
- [`NetPilot.Web`](../NetPilot.Web/README.md) — calls `AddNetPilotData` on startup, pointed at the same volume.

## Tests

[`test/NetPilot.Data.Tests`](../../test/NetPilot.Data.Tests) exercises the `Lite*Store` implementations against a real (temp-file) LiteDB instance.
