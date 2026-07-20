# NetPilot.Providers.TpLink

Thin adapter translating [`TpLink.Sdk`](../TpLink.Sdk/README.md)'s protocol models into [`NetPilot.Abstractions`](../NetPilot.Abstractions/README.md)'s router-agnostic shape. This is the template every future router brand's provider copies — `NetPilot.Core` never references `TpLink.Sdk` directly, only `IRouterProvider`.

## Purpose

`TpLinkRouterProvider` implements `IRouterProvider` by wrapping a `TpLinkRouterClient`. It's intentionally thin: connect/authenticate, map device records to `RouterDeviceSnapshot`, forward speed-limit writes — no domain logic (policy, fingerprinting, category seeding) lives here, that's all in `NetPilot.Core`.

## Declared capabilities

```csharp
new RouterCapabilities(
    SupportsSpeedLimit: true,
    SupportsDeviceCategorization: true,  // deviceType confirmed live, likely Fing-backed
    SupportsPriorityQos: false,          // enablePriority write path unconfirmed
    SupportsGuestNetworkInfo: true,      // isGuest confirmed present on every device record
    SupportsUsageTracking: true);        // trafficUsage confirmed present on every device record
```

`SupportsPriorityQos` is honestly reported `false` — the field is readable but its write path was never confirmed live, so NetPilot doesn't pretend to support it yet.

## Mapping notes

- **Connection medium** — derived from the device's `deviceTag` (`wired`, `5G`, `2.4G`/`iot_2.4G`) and `isGuest`, mapped to `ConnectionMedium`.
- **Hostname fallback** — this firmware reports the literal string `"NON_HOST"` (not blank) for a client that sent no DHCP hostname; the provider falls back to the router's own `deviceName` alias in that case, matching what the router's admin UI itself does.
- **Usage** — `TpLinkUsageParser.TryParseBytes` failures are logged at debug level and surfaced as `Usage: null` rather than thrown, since a single unparseable reading shouldn't fail the whole poll.
- **Router info is a known stub.** `GetRouterInfoAsync` returns the configured host but `"unknown (unverified endpoint)"` for model/firmware — that endpoint was never live-verified against the real router, so it's flagged rather than guessed.

## DI registration

```csharp
services.AddTpLinkProvider();
```

Registers `TpLinkRouterProvider` as the singleton `IRouterProvider`. v1 uses an ordinary compile-time registration — no dynamic plugin loading. `RouterProviderRegistry` in `NetPilot.Core` assumes exactly one registered provider.

## Consumers

- [`NetPilot.Agent`](../NetPilot.Agent/README.md) and [`NetPilot.Web`](../NetPilot.Web/README.md) — both call `AddTpLinkProvider()` on startup and resolve the shared `IRouterProvider`.

## Adding a second router brand

Copy this project's shape: implement `IRouterProvider` against that brand's own protocol client, report `RouterCapabilities` honestly, and register it with its own `AddXProvider()` extension method. `NetPilot.Core` requires no changes.
