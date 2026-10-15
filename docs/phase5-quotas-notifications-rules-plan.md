# NetPilot — Phase 5 Plan: Daily Quotas, Web Push Notifications, Rules Engine

**Status:** T1–T5 implemented (timezone, quota model/UI, enforcement, notification server, Web Push client); T6 supplies HTTPS access/deployment docs. Live HTTPS/Android checks remain deployment verification. T7 rules engine and T8 LAN-traffic exclusion are planned. This document retains the original design sketches; see [server notifications](server-notifications.md) for implemented notification behaviour.

**Decisions taken with the user**

- Client is **Android Chrome** (web push works in a normal tab; no PWA install required, but we ship a manifest anyway).
- Deployed on the home Proxmox server (CT 109, `192.168.1.24`). Plain HTTP on the LAN is **not** a secure context, so push needs HTTPS (see §6).
- The action taken when a device exceeds its quota is **flexible**: `NotifyOnly`, `Throttle(kbps)`, or `Block`, configurable globally, per category, and per device.
- Timezone is **Africa/Cairo** for "per day" boundaries. Stored as a config value (`NetPilot:TimeZone`, default `Africa/Cairo`), not hardcoded.

## 1. Preconditions and risks

1. **Usage counter is not live-verified** (`phase2-usage-tracking-feature.md` §2, §6). Enforcement on wrong numbers would throttle or block innocent devices. Therefore every quota and rule ships with a **dry-run** mode, default **on** for the first deploy: it evaluates, logs "would have done X", notifies, but does not write to the router. Enforcement is switched on per quota/rule after the user has compared numbers for a few days.
2. **Timezone keys implemented in T1.** Cairo midnight differs from UTC by 2–3 h (DST applies); current keys use `UsagePeriodClock`. Existing history rows stay as-is (UTC keys); document the one-time boundary overlap.
3. **Two-session bug** (`phase3-wireless-management-plan.md` §6.1). Quota/rule actions that touch the router run **only in the Agent**. The Web dashboard edits quota/rule *data* and never writes to the router for them.
4. **Block has no read/list endpoint** (`phase4-block-list-live-findings.md`). NetPilot cannot verify blocked state; it relies on `Device.IsBlocked` + `BlockListToken`. Throttle is the safer default action because it reuses the reconciled speed-limit path.
5. **Lockout.** Never auto-block the device the user manages from. A **protected devices** list is exempt from Block and Throttle (notify still allowed).

## 2. Quotas

### 2.1 Model (`NetPilot.Core/Quota/`)

```csharp
enum QuotaScope { Default, Category, Device }
enum QuotaAction { NotifyOnly, Throttle, Block }

record QuotaPolicy
{
    string Id;                  // "default" | "cat:{categoryKey}" | "dev:{mac}"
    QuotaScope Scope;
    string? CategoryKey;
    MacAddress? Mac;
    long? DailyBytes;           // null = no daily cap
    long? MonthlyBytes;         // null = no monthly cap
    bool Exempt;                // true = never evaluated (overrides lower-precedence scopes)
    QuotaAction Action;         // taken at 100%
    int? ThrottleKbps;          // required when Action == Throttle
    int[] WarnPercents;         // default [80]; notify at each, once per period
    bool DryRun;                // default true
}
```

**Resolution:** `Device` policy → `Category` policy → `Default` policy (first match wins; an `Exempt` match ends evaluation as "no quota"). Mirrors the existing override-beats-category pattern in `DevicePolicy`.

### 2.2 State (`NetPilot.Data`)

`QuotaPeriodState` — one row per `Mac|periodKey` (day and month), holding: `WarnedPercents`, `ExceededNotified`, `ActionAppliedAtUtc`, `ActionKind`, `ExtraAllowanceBytes` (from the "+1 GB today" button), `PriorLimit` (to restore on revert). This is what makes every threshold fire **once per period**.

### 2.3 Service

`QuotaEnforcementService` (Core), invoked by `Worker` right after `UsageTrackingService.TrackAsync`:

1. For each device with usage state: resolve policy, compute effective cap (`DailyBytes + ExtraAllowanceBytes`).
2. Crossing a warn percent not yet in `WarnedPercents` → emit `QuotaWarning` event.
3. Crossing 100% and not yet actioned → emit `QuotaExceeded`; if not `DryRun` and device not protected, apply action via existing paths (`SetSpeedLimitAsync` through the policy override mechanism for Throttle; `BlockDeviceAsync` for Block); record `PriorLimit`/token.
4. On period rollover (Cairo midnight), for every actioned device: revert (restore limit / unblock), log `QuotaReverted`.
5. Adaptive polling hint: devices ≥ 80% are flagged so the Worker can poll at 30 s instead of 180 s for just that tick cadence (second phase; plain 180 s first).

All events go to the activity log (`ActivityEventType` gains `QuotaWarning`, `QuotaExceeded`, `QuotaActionApplied`, `QuotaReverted`, `QuotaDryRun`) and to the notification pipeline (§3).

### 2.4 Dashboard

New **Quotas** tab (new component, not more lines in `Home.razor`): default quota editor, per-category table, per-device overrides (cap, action, throttle Kbps, exempt, dry-run), today's progress bars per device, protected-devices list.

## 3. Notifications

### 3.1 Abstractions (`NetPilot.Core/Notifications/`)

```csharp
record NotificationMessage(string Title, string Body, string? Url, string Tag, NotificationAction[] Actions);
interface INotificationChannel { string Id { get; } Task SendAsync(NotificationMessage m, CancellationToken ct); }
```

`NotificationDispatcher` fans a message out to all enabled channels, applies **quiet hours** (config, default off) and **de-duplication by `Tag`** (e.g. `quota:{mac}:{day}:80`), logs to `NotificationLog`.

### 3.2 Web push channel (`NetPilot.Providers`-style adapter or `NetPilot.Notifications.WebPush` project)

- VAPID keypair generated on first run, stored in the data volume beside the Data Protection keys. Never in source control.
- `PushSubscription` documents (endpoint, p256dh, auth, label, createdAt) in LiteDB; written by Web, read by Agent (shared DB, same as everything else).
- Sender uses a maintained WebPush NuGet package; 404/410 from the push service deletes the subscription.
- Message example: "iPhone-Islam: 4.8 of 5 GB used today (96%)". Tap opens `/devices/{mac}`.
- **Notification actions** (Android supports them): "+1 GB today", "Unblock / Restore now". These call token-authorized Web endpoints that write `ExtraAllowanceBytes` / request a revert; the **Agent** applies it on its next tick (keeps router writes in the Agent).

### 3.3 Web side (`NetPilot.Web`)

- `wwwroot/sw.js` (push + notificationclick + actions), `wwwroot/manifest.webmanifest`, JS interop to request permission and subscribe, "Enable notifications on this device" button in the Notifications tab, a "Send test notification" button, list of subscribed devices with remove.
- T5 action endpoints use scoped, expiring, single-use tokens; there is no dashboard login. Keep access private (see §6).

### 3.4 Later channels

`INotificationChannel` also allows Telegram/ntfy as a fallback. Not in this phase.

## 4. Rules engine

Built **after** quotas work in dry-run, so the generic model is shaped by a real first user. The built-in quota behavior becomes "quota rules" expressed in the same engine only if that stays simple; otherwise quotas remain a first-class feature and the engine covers everything else. Decide at T7 kickoff.

### 4.1 Model

```
Rule { Id, Name, Enabled, DryRun, Scope(all|category|device list), Trigger, Conditions[], Actions[], CooldownMinutes, OncePerPeriod(day|month|none), RevertPolicy }
```

**Triggers (v1):** `UsageOver(period, bytes|percentOfQuota)`, `TimeWindow(start, end, days, tz)`, `DeviceJoined`, `DeviceOnline/Offline`. **Later:** household total over X, idle for N days, sustained rate over X for N min (needs a live-rate field from the provider — check snapshot first), category-specific spikes.

**Actions (v1):** `Notify`, `Throttle(kbps)`, `Block`, `Unblock`, `RestoreLimit`, `SetWireless(guest|iot, enabled)` (writes already exist), each with optional `RevertAfter` / `RevertAtPeriodEnd`.

### 4.2 Evaluation

`RuleEvaluationService` in the Agent loop, over a context (device snapshot + usage state + now-in-Cairo). Fires write `RuleFiring` rows (rule, subject, time, outcome incl. dry-run) so the dashboard can answer "why was this device throttled?".

Safety rails: per-tick cap on automatic router writes; protected-devices exemption; cooldown/once-per-period; revert state persisted so a restart never strands a throttled/blocked device.

### 4.3 Starter rule templates

1. Over daily quota → throttle, revert at midnight.
2. Kids' devices 22:00–07:00 → throttle hard, restore after.
3. New unknown device joins → notify + low "guest" limit until approved.
4. TV/console over 10 GB/day → notify (binge alert).
5. IP camera upload far above its own baseline → notify (possible compromise).
6. Household monthly total at 80% of ISP cap → notify, tighten default limits.
7. Device silent 14 days → notify, offer cleanup.
8. Guest WiFi auto-off after 2 hours.

## 5. Data and API additions

- LiteDB collections: `quota_policies`, `quota_period_state`, `push_subscriptions`, `notification_log`, `rules`, `rule_firings`, `protected_devices`. All absent in existing DBs; LiteDB creates on first write, no migration.
- `NetPilot.Core` interfaces `IQuotaStore`, `IPushSubscriptionStore`, `IRuleStore`, … implemented in `NetPilot.Data` (same `IXStore`/`LiteXStore` pattern).
- Config: `NetPilot:TimeZone`, `NetPilot:Quota:DryRunDefault`, `NetPilot:Notifications:QuietHours`.

## 6. HTTPS and access

Android Chrome requires a **secure context** for service workers / push subscriptions. [HTTPS access](https-access.md) gives reproducible setup steps and troubleshooting:

| Option | Use | Constraint |
|---|---|---|
| **Tailscale Serve (recommended)** | Trusted HTTPS at `<name>.<tailnet>.ts.net`, home and away | Official Docker image in userspace mode; no `/dev/net/tun` passthrough; phone must join the tailnet |
| Caddy internal CA | LAN-only HTTPS | Install Caddy's root CA on the phone and preserve its data |
| Chrome insecure-origin flag | One-off test only | Insecure and version-dependent; never a permanent setup |

T6 ships optional Compose overrides; it does not enroll a real account or change the live CT. Base LAN access on HTTP `:8080` is preserved. **The dashboard has no authentication: never expose it to the public Internet or enable Tailscale Funnel.** T5 action tokens authorize only their scoped actions, not dashboard access.

Push sends need outbound Internet from Agent/Web. The phone needs HTTPS reachability for enrollment, dashboard access and notification actions; the push-service/OS delivery path is separate. Use the [deployment verification checklist](deployment.md#post-deploy-verification) before treating phone delivery as verified.

## 7. Open items

- `trafficUsage` unit/reset verification (blocks turning dry-run off).
- Whether the TP-Link snapshot exposes a live rate (needed for the sustained-rate trigger).
- Activate and verify the selected HTTPS option (§6); Tailscale is recommended, not yet enrolled by T6.
- Whether quotas become rules in the engine or stay separate (decide at T7).

## 8. Delegation to Codex — task breakdown

Each task = one Codex run, reviewed as a diff, one commit. Tests alongside, `dotnet build` + `dotnet test` green before review.

| # | Task | Projects | Done when |
|---|---|---|---|
| T1 | Timezone-aware day keys + `NetPilot:TimeZone` config (default Africa/Cairo); update `UsageTrackingService` and `UsageQuery` callers | Core, Agent, Web, tests | Tests cover rollover at Cairo midnight incl. DST; Usage tab shows Cairo days |
| T2 | Quota model, stores, resolution, protected devices; Quotas tab (default/category/device editors, progress bars) | Core, Data, Web | Resolution order tested; UI saves/loads; no router calls |
| T3 | `QuotaEnforcementService`: thresholds once-per-period, dry-run, Throttle/Block/NotifyOnly actions, midnight revert, restart-safe state, activity events | Core, Agent | Tests with `FakeRouterProvider`: warn once, exceed once, revert once, protected device skipped, dry-run writes nothing |
| T4 | Notification abstractions + `NotificationDispatcher` + `PushSubscription` store + VAPID keys + WebPush sender, dead-subscription cleanup | Core, Data, new notifications project, Agent | Sender unit-tested against a fake push service; keys persist in volume |
| T5 | PWA + subscribe flow in Web: `sw.js`, manifest, permission/subscribe button, test-notification button, action endpoints (+1 GB, restore) with scoped single-use tokens | Web | Subscribes on Android Chrome over HTTPS; test push arrives; action buttons change state |
| T6 | HTTPS access docs + optional userspace Tailscale/Caddy Compose overrides; update `docs/deployment.md`, README, stale `AGENTS.md` | deploy, docs | Documented, reproducible steps |
| T7 | Rules engine: model, evaluator, v1 triggers/actions, templates, firing history view, rules UI | Core, Data, Agent, Web | Starter templates 1–3 working in dry-run; firing history visible |

Order: **T1 → T2 → T3** gives working quotas with dashboard alerts; **T4 → T5 → T6** adds phone push; **T7** generalizes. After T3 deploys, run in dry-run for several days before enabling enforcement.

## 9. T8 — Exclude LAN traffic from Internet usage (Jellyfin → TV)

**Problem (confirmed by the user):** the router counts *all* traffic per device, local and Internet alike. A Jellyfin stream to the TV is counted on the server **and** on the TV, though it never touches the Internet. Quotas and rules must act on Internet bytes only, so T8 must land **before quota enforcement leaves dry-run**.

**Model:** `internetBytes(device) = routerBytes(device) − lanBytes(device)`. LAN bytes between a pair of devices are subtracted from **both** endpoints, because the router counted them on both.

**Source of LAN bytes (`ILocalTrafficSource`, cumulative bytes per peer IP):**
- Preferred: nftables/iptables per-destination counters on the Jellyfin host (private-range destinations only, so remote viewers streaming over the Internet stay counted as real usage), exposed by a tiny read-only endpoint. Exact.
- Fallback: Jellyfin `/Sessions` API estimate (client IP, bitrate × play time). Approximate, coarse at the 180 s tick.
- Alternative with no code: put TV and server on one switch so the traffic never reaches the router.

**Rules:**
- Per-device usage splits into Internet bytes and LAN bytes. The Usage tab shows both; quotas, rules and notifications read Internet bytes only.
- LAN deltas use the same reset/rollover handling as router counters; the Internet delta is clamped at zero.
- Per-device "LAN-only server" flag excludes a device from Internet usage entirely.
- Source unreachable ⇒ fall back to router bytes and log a warning (never silently drop usage).

**Open questions:** where Jellyfin runs (CT/VM/Docker, IP), TV on Wi-Fi or wired, any remote viewers, whether shell access to the Jellyfin host is available (decides nftables vs API estimate).

**T8 decisions (user answers):** Jellyfin runs in a Proxmox CT/VM on the same server; the Samsung TV is on Wi-Fi; Jellyfin is home-only (no remote viewers); guest shell access unknown. Because Jellyfin shares the Proxmox host, the plan is **host-level nftables bridge-family counters on `vmbr0`** (per Jellyfin MAC → client IP, no access inside the guest needed), read by NetPilot through a small read-only HTTP endpoint on the Proxmox host restricted to CT 109. The Jellyfin API estimate stays as a fallback. Since there are no remote viewers, the Jellyfin box's own router counter is effectively all-LAN and can also be excluded with the "LAN-only server" flag as a first, zero-infrastructure step.
