# NetPilot — Phase 2 Implementation Plan: Per-MAC Usage Tracking

**Read first:** `docs/phase2-usage-tracking-plan.md` — the conceptual design (why poll-delta accumulation, why reset detection is trigger-agnostic). This doc is the concrete build ticket derived from it: exact files, exact signatures, exact order. Written against the actual code as of this commit (reviewed: `IRouterProvider`, `TpLinkDeviceRecord`, `TpLinkRouterProvider`, `PolicyReconciliationService`, `Worker`, `NetPilotDatabase`, `LiteDeviceStore`/`LitePolicyStore`, `Home.razor`, existing test fakes).

**Unresolved input, handled by design, not by blocking:** the exact wire format of `trafficUsage` (bytes? KB? formatted string?) is still unconfirmed live. Rather than gate implementation on a live check, this plan captures the raw value losslessly and isolates unit-guessing behind one small, swappable parser (`TpLinkUsageParser`) — see step 2. If a later live check shows the assumption is wrong, only that one file changes.

---

## File-by-file changes, in dependency order

### 1. `src/TpLink.Sdk/Models/LenientStringConverter.cs` (new)

Same shape as the existing `LenientIntConverter.cs` in the same folder, but normalizes number-or-string JSON tokens to a `string?` instead of parsing to `int?`. Needed because `trafficUsage`/`onlineTime` might arrive as a bare number or a numeric string (same inconsistency already confirmed live for `downloadLimit`/`uploadLimit`), and — unlike those two fields — we don't yet know if the value could also be a non-numeric formatted string (e.g. `"1.2 GB"`), so capturing as a string first (never throwing, never silently truncating) is the safe default.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TpLink.Sdk.Models;

/// <summary>
/// Reads a JSON number or string token as a string, unmodified. Used for fields whose
/// exact wire format isn't fully confirmed (trafficUsage, onlineTime) — captures the raw
/// value losslessly so parsing logic can live separately and be swapped out without
/// touching the model or the transport layer.
/// </summary>
public class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a lenient string field.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
```

### 2. `src/TpLink.Sdk/Models/TpLinkDeviceRecord.cs` (edit)

Add two properties (near `SpeedLimitOnline`/`TimePeriod`):

```csharp
[JsonPropertyName("trafficUsage")]
[JsonConverter(typeof(LenientStringConverter))]
public string? TrafficUsageRaw { get; set; }

[JsonPropertyName("onlineTime")]
[JsonConverter(typeof(LenientStringConverter))]
public string? OnlineTimeRaw { get; set; }
```

### 3. `src/TpLink.Sdk/TpLinkUsageParser.cs` (new)

The one place that encodes the unit assumption. Primary path: treat the raw value as a plain integer count of **bytes** — this matches the pattern of every other numeric field on this endpoint (`downloadLimit`/`uploadLimit` are plain Kbps integers, sometimes string-wrapped) and is the most likely shape. Fallback path: handle a human-formatted string (`"1.2 GB"`, `"512 KB"`, etc.) in case the primary assumption is wrong, so the parser degrades gracefully instead of just returning null for a plausible alternate format.

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace TpLink.Sdk;

/// <summary>
/// Parses the raw trafficUsage string into a byte count. The unit is NOT yet confirmed
/// live (see docs/phase2-usage-tracking-plan.md, open item #1) — this defaults to
/// "plain integer = bytes", matching every other numeric field on this endpoint, with a
/// formatted-string fallback in case that assumption is wrong. This is the single place
/// to fix if a live capture shows a different unit (e.g. multiply by 1024 for KB).
/// </summary>
public static partial class TpLinkUsageParser
{
    public static bool TryParseBytes(string? raw, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
        {
            bytes = plain;
            return true;
        }

        var match = FormattedSizeRegex().Match(trimmed);
        if (!match.Success)
            return false;

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "B" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            _ => 0L
        };
        if (multiplier == 0L)
            return false;

        bytes = (long)(value * multiplier);
        return true;
    }

    [GeneratedRegex(@"^([\d.]+)\s*(B|KB|MB|GB|TB)$", RegexOptions.IgnoreCase)]
    private static partial Regex FormattedSizeRegex();
}
```

### 4. `src/NetPilot.Abstractions/UsageSnapshot.cs` (new)

```csharp
namespace NetPilot.Abstractions;

/// <summary>
/// Raw usage counter as reported by the router on this poll — cumulative, may reset
/// unpredictably (cause varies by router/firmware and NetPilot doesn't rely on knowing
/// which — see UsageTrackingService). Null on RouterDeviceSnapshot means either the
/// provider doesn't support usage tracking (check RouterCapabilities.SupportsUsageTracking
/// first) or it does but couldn't parse this particular reading.
/// </summary>
public record UsageSnapshot(long TotalBytes);
```

v1 models a single combined counter, matching the single `trafficUsage` field name (unlike `downloadLimit`/`uploadLimit`, which are already split). If a live check later shows separate download/upload counters, extend this to `UsageSnapshot(long DownloadBytes, long UploadBytes)` — additive change to this record, `UsageTrackingService`, and the two Data documents in step 8; nothing else in the design depends on it being combined vs. split.

### 5. `src/NetPilot.Abstractions/RouterDeviceSnapshot.cs` (edit)

Add one field:

```csharp
public record RouterDeviceSnapshot(
    string MacAddress,
    string IpAddress,
    string Hostname,
    string? RawCategory,
    ConnectionInfo Connection,
    SpeedLimitState CurrentLimit,
    UsageSnapshot? Usage);   // new — null if unsupported/unparseable this tick
```

**This changes a positional record's constructor arity** — every call site must be updated (see step 6 and the test file in step 6b). There are exactly two production call sites (`TpLinkRouterProvider.ToSnapshot`) and one test helper (`PolicyReconciliationServiceTests.MakeSnapshot`).

### 6. `src/NetPilot.Abstractions/RouterCapabilities.cs` (edit)

```csharp
public record RouterCapabilities(
    bool SupportsSpeedLimit,
    bool SupportsDeviceCategorization,
    bool SupportsPriorityQos,
    bool SupportsGuestNetworkInfo,
    bool SupportsUsageTracking);   // new
```

**Also a positional record — every construction site needs the new argument.** Production: `TpLinkRouterProvider.Capabilities`. Tests: `PolicyReconciliationServiceTests.FullCapabilities` and the `noCategoryCapabilities = FullCapabilities with { ... }` line (the `with` expression doesn't need editing, just the base record's constructor call).

### 6b. `src/NetPilot.Providers.TpLink/TpLinkRouterProvider.cs` (edit)

- Add `ILogger<TpLinkRouterProvider>` to the constructor (DI resolves it automatically, no registration change needed — logging is already wired by `Host.CreateApplicationBuilder`/`WebApplication.CreateBuilder`).
- `Capabilities`: add `SupportsUsageTracking: true`.
- `ToSnapshot`: parse usage, log once per tick per device on parse failure (not per-poll spam — a `LogDebug` is fine, this isn't actionable enough for `LogWarning`):

```csharp
private UsageSnapshot? ToUsage(TpLinkDeviceRecord record)
{
    if (!TpLinkUsageParser.TryParseBytes(record.TrafficUsageRaw, out var bytes))
    {
        if (record.TrafficUsageRaw is not null)
            _logger.LogDebug("Could not parse trafficUsage {Raw} for {Mac}", record.TrafficUsageRaw, record.Mac);
        return null;
    }
    return new UsageSnapshot(bytes);
}
```

and add `Usage: ToUsage(record)` as the last argument in the existing `ToSnapshot` record construction.

### 7. `src/NetPilot.Core/Enforcement/ActivityEventType.cs` (edit)

Add one value: `UsageCounterReset`.

### 8. `src/NetPilot.Core/Usage/` (new folder — mirrors `Policy/` and `Enforcement/`)

**`DeviceUsageState.cs`** (mutable running state, one per MAC — mirrors `Device.cs`):

```csharp
using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public class DeviceUsageState
{
    public required MacAddress Mac { get; init; }
    public long LastRawCounterBytes { get; set; }
    public DateTimeOffset? LastPollAtUtc { get; set; }   // null = never observed yet
    public string CurrentMonthKey { get; set; } = "";
    public long CurrentMonthBytes { get; set; }
}
```

**`UsageHistoryEntry.cs`** (finalized, immutable, one per Mac+Month):

```csharp
using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public record UsageHistoryEntry(MacAddress Mac, string MonthKey, long TotalBytes, DateTimeOffset FinalizedAtUtc);
```

**`IUsageStore.cs`**:

```csharp
using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public interface IUsageStore
{
    Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct);
    Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct);
    Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct);
    Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct);
    Task<IReadOnlyList<UsageHistoryEntry>> GetHistoryAsync(MacAddress mac, int monthsBack, CancellationToken ct);
}
```

**`UsageTrackingService.cs`** — the actual algorithm from `phase2-usage-tracking-plan.md` §3:

```csharp
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;

namespace NetPilot.Core.Usage;

/// <summary>
/// Turns the router's raw, reset-prone usage counter into a durable running monthly total.
/// Reset detection is per-device and cause-agnostic — see phase2-usage-tracking-plan.md §3:
/// a counter that's lower than last observed is treated as a reset regardless of why.
/// Called once per tick with the same snapshot PolicyReconciliationService already fetched
/// — no extra HTTP call to the router.
/// </summary>
public class UsageTrackingService(IUsageStore usageStore, IActivityLogStore activityLog)
{
    public async Task TrackAsync(IReadOnlyList<RouterDeviceSnapshot> snapshots, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var monthKey = MonthKeyFor(now);

        foreach (var snapshot in snapshots)
        {
            if (snapshot.Usage is null)
                continue; // provider doesn't support usage tracking, or couldn't parse this tick

            var mac = new MacAddress(snapshot.MacAddress);
            var state = await usageStore.FindStateAsync(mac, ct)
                ?? new DeviceUsageState { Mac = mac, CurrentMonthKey = monthKey };

            if (state.CurrentMonthKey != monthKey)
            {
                if (!string.IsNullOrEmpty(state.CurrentMonthKey))
                {
                    await usageStore.AppendHistoryAsync(
                        new UsageHistoryEntry(mac, state.CurrentMonthKey, state.CurrentMonthBytes, now), ct);
                }
                state.CurrentMonthKey = monthKey;
                state.CurrentMonthBytes = 0;
            }

            var current = snapshot.Usage.TotalBytes;

            if (state.LastPollAtUtc is null)
            {
                // First time we've ever seen this device's counter. Do NOT add `current` as
                // this month's usage — it's likely a pre-existing lifetime/session total, not
                // usage that happened this month. Adopt it as the baseline only.
            }
            else if (current >= state.LastRawCounterBytes)
            {
                state.CurrentMonthBytes += current - state.LastRawCounterBytes;
            }
            else
            {
                // Counter went backwards — reset, cause unknown and irrelevant (restart, daily
                // rollover, whatever). Assume it restarted at 0 and resume from here.
                state.CurrentMonthBytes += current;
                await activityLog.AppendAsync(new ActivityLogEntry(now, ActivityEventType.UsageCounterReset, mac,
                    $"Usage counter reset detected (was {state.LastRawCounterBytes} bytes, now {current}) — resumed accumulation from new baseline"), ct);
            }

            state.LastRawCounterBytes = current;
            state.LastPollAtUtc = now;
            await usageStore.UpsertStateAsync(state, ct);
        }
    }

    private static string MonthKeyFor(DateTimeOffset utc) => utc.ToString("yyyy-MM");
}
```

### 9. `src/NetPilot.Core/Enforcement/PolicyReconciliationService.cs` (edit — small, return-type change only)

Currently `ReconcileAsync` returns `Task` and fetches `snapshots` internally but never exposes them. Change the return type so the tick's snapshot can be reused for usage tracking without a second HTTP call:

```csharp
public async Task<IReadOnlyList<RouterDeviceSnapshot>> ReconcileAsync(IRouterProvider provider, CancellationToken ct)
{
    var snapshots = await provider.GetDevicesAsync(ct);
    // ...unchanged body...
    await MarkMissingDevicesOfflineAsync(seenMacs, ct);
    return snapshots;
}
```

Only the signature (`Task` → `Task<IReadOnlyList<RouterDeviceSnapshot>>`) and the final `return snapshots;` change — every existing line of logic stays as-is. This is source-compatible with every current caller (`Worker.cs`, `Home.razor`, all of `PolicyReconciliationServiceTests`) since none of them currently do anything with a return value — `await service.ReconcileAsync(...)` still compiles unchanged when the awaited task now carries a result.

### 10. `src/NetPilot.Data/Documents/UsageStateDocument.cs` (new)

```csharp
using LiteDB;

namespace NetPilot.Data.Documents;

public class UsageStateDocument
{
    [BsonId]
    public string Mac { get; set; } = "";
    public long LastRawCounterBytes { get; set; }
    public DateTimeOffset? LastPollAtUtc { get; set; }
    public string CurrentMonthKey { get; set; } = "";
    public long CurrentMonthBytes { get; set; }
}
```

### 11. `src/NetPilot.Data/Documents/UsageHistoryDocument.cs` (new)

```csharp
using LiteDB;

namespace NetPilot.Data.Documents;

public class UsageHistoryDocument
{
    [BsonId]
    public string Id { get; set; } = "";   // $"{Mac}|{MonthKey}"
    public string Mac { get; set; } = "";
    public string MonthKey { get; set; } = "";
    public long TotalBytes { get; set; }
    public DateTimeOffset FinalizedAtUtc { get; set; }
}
```

### 12. `src/NetPilot.Data/LiteUsageStore.cs` (new — mirrors `LiteDeviceStore.cs`/`LitePolicyStore.cs`)

```csharp
using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteUsageStore : IUsageStore
{
    private readonly ILiteCollection<UsageStateDocument> _state;
    private readonly ILiteCollection<UsageHistoryDocument> _history;

    public LiteUsageStore(NetPilotDatabase db)
    {
        _state = db.GetCollection<UsageStateDocument>("usage_state");
        _history = db.GetCollection<UsageHistoryDocument>("usage_history");
    }

    public Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct)
    {
        var doc = _state.FindById((string)mac);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct)
    {
        IReadOnlyList<DeviceUsageState> states = _state.FindAll().Select(ToDomain).ToList();
        return Task.FromResult(states);
    }

    public Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct)
    {
        _state.Upsert(ToDocument(state));
        return Task.CompletedTask;
    }

    public Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct)
    {
        _history.Upsert(new UsageHistoryDocument
        {
            Id = $"{entry.Mac}|{entry.MonthKey}",
            Mac = entry.Mac,
            MonthKey = entry.MonthKey,
            TotalBytes = entry.TotalBytes,
            FinalizedAtUtc = entry.FinalizedAtUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageHistoryEntry>> GetHistoryAsync(MacAddress mac, int monthsBack, CancellationToken ct)
    {
        IReadOnlyList<UsageHistoryEntry> entries = _history.Find(h => h.Mac == (string)mac)
            .OrderByDescending(h => h.MonthKey)
            .Take(monthsBack)
            .Select(h => new UsageHistoryEntry(new MacAddress(h.Mac), h.MonthKey, h.TotalBytes, h.FinalizedAtUtc))
            .ToList();
        return Task.FromResult(entries);
    }

    private static DeviceUsageState ToDomain(UsageStateDocument doc) => new()
    {
        Mac = new MacAddress(doc.Mac),
        LastRawCounterBytes = doc.LastRawCounterBytes,
        LastPollAtUtc = doc.LastPollAtUtc,
        CurrentMonthKey = doc.CurrentMonthKey,
        CurrentMonthBytes = doc.CurrentMonthBytes
    };

    private static UsageStateDocument ToDocument(DeviceUsageState state) => new()
    {
        Mac = state.Mac,
        LastRawCounterBytes = state.LastRawCounterBytes,
        LastPollAtUtc = state.LastPollAtUtc,
        CurrentMonthKey = state.CurrentMonthKey,
        CurrentMonthBytes = state.CurrentMonthBytes
    };
}
```

### 13. `src/NetPilot.Data/ServiceCollectionExtensions.cs` (edit)

Add one line inside `AddNetPilotData`:

```csharp
services.AddSingleton<IUsageStore, LiteUsageStore>();
```

(needs `using NetPilot.Core.Usage;` added to the top of the file)

### 14. `src/NetPilot.Agent/Program.cs` (edit)

Add one line:

```csharp
builder.Services.AddSingleton<UsageTrackingService>();
```

(needs `using NetPilot.Core.Usage;`)

### 15. `src/NetPilot.Agent/Worker.cs` (edit)

Add `UsageTrackingService usageTrackingService` to the primary constructor parameter list, and change the tick body:

```csharp
else
{
    var snapshots = await reconciliationService.ReconcileAsync(routerProvider, stoppingToken);
    await usageTrackingService.TrackAsync(snapshots, stoppingToken);
}
```

(needs `using NetPilot.Core.Usage;`)

### 16. `src/NetPilot.Web/Program.cs` (edit)

Add one line (same as step 14):

```csharp
builder.Services.AddSingleton<UsageTrackingService>();
```

### 17. `src/NetPilot.Web/Components/Pages/Home.razor` (edit — dashboard panel)

Follow the existing tab pattern exactly (`Tab.Devices`/`Tab.Policies`/`Tab.Activity` → add `Tab.Usage`):

- `@inject IUsageStore UsageStore` added alongside the other injects.
- New enum case `Usage` in `private enum Tab`.
- New tab button: `<button type="button" class="np-tab @(_activeTab == Tab.Usage ? "active" : "")" @onclick="() => _activeTab = Tab.Usage">Usage</button>`.
- New `@if (_activeTab == Tab.Usage)` block: reuse `_devices` (already loaded) joined with `await UsageStore.GetAllStatesAsync(...)`, sorted by `CurrentMonthBytes` descending. Reuse the existing `np-device-row`/`np-group` CSS classes from the Devices tab rather than inventing new ones — same visual language, this dashboard already has a CSS system for device rows.
- New field `_usageStates` (populate in `RefreshLiveDataAsync`, same place `_devices`/`_activity` are refreshed).
- New helper `FormatBytes(long bytes)` → human-readable string (`"1.2 GB"`, `"340 MB"`, etc.) — same rounding style as the existing `KbpsToMbps` helper.
- Optional but recommended for consistency: in `RefreshFromRouterAsync`, after `await ReconciliationService.ReconcileAsync(...)` now returns snapshots, also `await UsageTrackingService.TrackAsync(snapshots, CancellationToken.None);` so a manual "Refresh from router" click updates usage too, not just the Agent's background tick. Requires injecting `UsageTrackingService` into the page.

This step is the least mechanically specified because it's UI layout — deliberately left to match whatever the existing `np-*` CSS classes support rather than prescribing exact markup here.

---

## Tests to add

### `test/TpLink.Sdk.Tests/TpLinkUsageParserTests.cs` (new)

Table-driven, mirrors the style of `ModelParsingTests.cs`:

- Plain integer string (`"12345"`) → parses as that many bytes.
- Formatted string (`"1.2 GB"`, `"512 KB"`, `"3 B"`) → parses with the right multiplier.
- `null` / empty / whitespace → returns `false`.
- Garbage (`"n/a"`, `"unknown"`) → returns `false`, doesn't throw.

### `test/TpLink.Sdk.Tests/ModelParsingTests.cs` (edit)

The existing `LoadDeviceFixture` already contains `"onlineTime": "100"` and `"trafficUsage": "200"` — add assertions to the existing `LoadDeviceResponse_ParsesCameraFixture_WithStringKbpsFields` test (or a new dedicated test) confirming `device.TrafficUsageRaw == "200"` and `device.OnlineTimeRaw == "100"`.

### `test/NetPilot.Core.Tests/Fakes/InMemoryStores.cs` (edit — add one fake)

```csharp
public class InMemoryUsageStore : IUsageStore
{
    private readonly Dictionary<string, DeviceUsageState> _states = [];
    public List<UsageHistoryEntry> History { get; } = [];

    public Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct) =>
        Task.FromResult(_states.GetValueOrDefault((string)mac));

    public Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeviceUsageState>>(_states.Values.ToList());

    public Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct)
    {
        _states[(string)state.Mac] = state;
        return Task.CompletedTask;
    }

    public Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct)
    {
        History.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageHistoryEntry>> GetHistoryAsync(MacAddress mac, int monthsBack, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsageHistoryEntry>>(
            History.Where(h => h.Mac == mac).OrderByDescending(h => h.MonthKey).Take(monthsBack).ToList());
}
```

### `test/NetPilot.Core.Tests/Usage/UsageTrackingServiceTests.cs` (new — table-driven, mirrors `PolicyReconciliationServiceTests.cs` style)

Minimum cases to cover:

1. **`FirstObservation_AdoptsBaseline_NoDeltaCounted`** — a snapshot with `Usage.TotalBytes = 500_000` seen for the first time results in `CurrentMonthBytes == 0` after tracking (baseline adopted, not counted as usage).
2. **`CounterIncreases_AddsDeltaToCurrentMonth`** — two ticks, counter goes 1000 → 1500, `CurrentMonthBytes == 500` after the second tick.
3. **`CounterDecreases_TreatedAsReset_ResumesFromNewBaseline_LogsEvent`** — three ticks: 1000 (baseline) → 1500 (delta 500) → 300 (reset: add 300, not negative) → asserts `CurrentMonthBytes == 800` and an `ActivityEventType.UsageCounterReset` entry was logged exactly once.
4. **`MonthRollover_FinalizesPreviousMonth_ResetsRunningTotal`** — construct a `DeviceUsageState` directly via the fake store with `CurrentMonthKey` set to a past month and nonzero `CurrentMonthBytes`, run one tick, assert a `UsageHistoryEntry` was appended with the old month's total and the live state's `CurrentMonthBytes` reset to reflect only the new month's delta.
5. **`SnapshotWithNullUsage_IsSkipped`** — a snapshot with `Usage: null` (unsupported provider or failed parse) results in no store writes for that MAC.

`MakeSnapshot` test helper (in `PolicyReconciliationServiceTests.cs`) needs one new optional parameter, e.g. `UsageSnapshot? usage = null`, threaded into the new last positional argument of `RouterDeviceSnapshot` — reuse this same helper (or a copy in the new test file) rather than duplicating snapshot-construction logic.

---

## Suggested build order (so nothing references a type that doesn't exist yet)

1. `LenientStringConverter` → `TpLinkDeviceRecord` fields → `TpLinkUsageParser` → its tests. (`TpLink.Sdk` compiles standalone, verify with `dotnet test test/TpLink.Sdk.Tests`.)
2. `UsageSnapshot` → `RouterDeviceSnapshot` (+`Usage` field) → `RouterCapabilities` (+flag) — fix the two production call sites this breaks (`TpLinkRouterProvider`) and the test call sites (`PolicyReconciliationServiceTests`) in the same pass, since the build won't succeed otherwise.
3. `TpLinkRouterProvider` edits (logger, capability flag, `ToUsage`).
4. `NetPilot.Core/Usage/*` (state, history entry, `IUsageStore`, `UsageTrackingService`) + `ActivityEventType.UsageCounterReset`.
5. `PolicyReconciliationService.ReconcileAsync` return-type change.
6. `NetPilot.Data` (two documents, `LiteUsageStore`, DI registration).
7. `InMemoryUsageStore` fake + `UsageTrackingServiceTests`.
8. `NetPilot.Agent` wiring (`Program.cs`, `Worker.cs`).
9. `NetPilot.Web` wiring (`Program.cs`, `Home.razor` Usage tab).
10. `dotnet build` the whole solution, `dotnet test` both test projects.

No `dotnet` toolchain is available in this sandbox — steps that involve compiling/testing need to run in an environment that has it (matches the note already in `mvp-product-architecture.md` §11).

---

## What this plan deliberately does not decide

- **Exact byte-vs-formatted-string unit of `trafficUsage`** — isolated to `TpLinkUsageParser`; ships with a documented default assumption (plain integer = bytes) rather than blocking on a live check.
- **Split download/upload usage** — `UsageSnapshot` and the two Data documents model a single combined total for v1; splitting later is additive (see step 4's note).
- **UTC vs. local-timezone month boundary** — `MonthKeyFor` uses UTC; flag to the user before Phase 2 ships if their ISP billing cycle needs a different boundary. One-line change in `UsageTrackingService` if so.
- **Web UI visual design of the Usage tab** — step 17 specifies data flow and component reuse, not exact markup.
