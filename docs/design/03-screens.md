# 03 · Screen and interaction specifications

**Status legend:** Existing = implementation found; New UI = presentation over existing stores; New backend = new persistence/query/operation required. Every future capability below must be gated until implemented. Prototype behavior is simulated. The exhaustive existing-feature inventory is in [02](02-information-architecture.md).

## Universal screen states

These states apply to **every screen below**, including each Settings page and onboarding step; per-screen differences follow.

| State | Presentation / allowed behavior |
|---|---|
| Loading | Keep heading/nav/mode strip; skeleton matching card/row geometry; `aria-busy` on region; one polite “Loading devices” announcement; stop shimmer for reduced motion |
| Empty | Context-specific illustration + title + one CTA (listed below); filtered empty offers Clear filters, never asks to configure the router again |
| Error | Inline retained-data error with Retry, last successful timestamp, preserved form draft. Don't collapse the screen into a toast |
| Router offline | Persistent “Router unavailable · last successful read …”; cached read-only observations marked stale. Data-only saves allowed with pending receipt; direct router actions disabled with reason. DB errors and lost Blazor circuit are different failures |
| Dry-run | Feature-specific chip on policy, preview and events; “Would throttle” wording. Dry-run can still notify. Manual actions/base speed policies are outside quota/rule dry-run; confirmation says so |
| Mixed / unknown | Aggregate mode is Mixed if policies differ; forced rules mode overrides individual flags. Unknown until loaded or Web/Agent config mismatch; never assert safety from a Web banner alone |

## Home · `/`

Purpose: answer “Is the network working, what is consuming data, and does anything need me?” without opening a settings form.

| Phone ≤600 | Tablet 601–1199 | Desktop ≥1200 |
|---|---|---|
| Heading → household health → usage hero → attention → restrictions → top consumers → quick actions | Health full width; usage/attention side by side; consumers/restrictions side by side | 8-column usage/consumers; 4-column health/attention; restriction section below; quick action row |

- Household card: Home network / Archer AX53, router observation age, Agent last completed tick and next expected window, separate quota/rules modes. Heartbeat/receipt data is **New backend**. Never infer Agent health from Web uptime or saved connection.
- Hero: today **8.4 GB Internet / 15 GB daily household budget**; month **214 / 300 GB**. Household budget is **New backend**, optional; unset shows Set ISP budget, not a zero-cap meter. Show Cairo date and “Estimates; LAN verification pending.”
- Top consumers: TV 4.2, laptop 2.1, Pixel 1.4, PS5 .6, camera .1, server 0 GB; mini bars compare to top value, not a quota. All six sum to 8.4. LAN excluded 24 GB endpoint-total. Online now: 5 of 6; link to filtered list.
- Restrictions: current recorded/manual/base limits separately from live automatic claims and dry-run previews. Release opens the **specific owner** with remaining claims shown. The prototype includes a separate Enforcing scenario to demonstrate overlapping claims without suggesting live rollout is approved.
- Attention: near-quota warning; new device joined (review, not necessarily untrusted); LAN source unavailable/not verified; push not subscribed. Dismiss informational notices; health faults remain until resolved. New-device approval queue is a future workflow over current New device guard.
- Quick actions: Pause device/category for 15/30/60/custom minutes (**New backend temporary claim**, protected targets skipped); Add bonus GB (current token-bound quota action; general dashboard editor is **New backend**); Block (existing direct Web confirmation); Guest WiFi (existing direct write, capability-gated). Show target count, duration and timing before submit.
- Primary: contextual attention action, otherwise Pause internet. Secondary: View usage / See devices. Empty: “Let’s find your devices” → Connect router. Failure: keep last summary with timestamp; no forecast on stale data.
- Copy: “Your automations are observing.” / “Quotas: Dry-run. Rules: Forced dry-run. Manual controls still change the router.”

## Devices · `/devices`

Purpose: find a device, understand state, take a scoped action.

- Phone: category section headers, cards with 24px icon, name, online age, Internet today, restriction chip and overflow. Tablet: two-column cards. Desktop: semantic table with sortable name/category/online/usage/limit columns; supporting MAC/IP/medium beneath name. No horizontal page scrolling.
- Search name/hostname/MAC/IP; category, online/offline, restricted, protected, LAN-only; group Category / None / Person (**Person New backend**, hidden until available). URL persists filters. Don't default to online-only and hide offline devices silently; show counts.
- Multi-select checkboxes use stable MAC; toolbar says “3 selected”; bulk policy/temporary pause preview skips protected devices and lists failures individually. Keep selections across visible-page changes; Select all means filtered results with explicit count, never silently all devices.
- Primary: select device name → detail. Secondary: Refresh from router (existing direct reconciliation, warning that it may also apply configured speed policies), block/unblock, bulk select. A passive “Refresh saved data” should reread stores without logging into router.
- Empty: “No devices discovered” → Refresh from router. No matches → Clear filters. Offline: cached list, no live badges, last-seen remains factual. Dry-run previews use separate muted metadata, not a Blocked chip.

## Device detail · `/devices/{Mac}`

Purpose: identity, usage and a complete explanation of restrictions in one place.

| Phone | Tablet | Desktop |
|---|---|---|
| Back → identity/state → quick actions → usage → quota → Why restricted → rules/schedule → settings/history | Identity full width; usage + quota grid; remaining panels stacked | 8-column usage/history, 4-column quota/claims; identity/actions full width |

- Identity: friendly name (80-character proposed maximum), hostname, canonical MAC/IP (copy button), router-reported category and local override distinct, icon fallback; edit name/category/icon is **New UI + field-ownership backend** to prevent discovery overwriting it. Categories remain arbitrary strings. Unidentified device uses hostname then MAC.
- Observed state: wired/WiFi band, online/last seen, desired/base limit vs last router-reported limit + observed time. Blocked says “NetPilot record; router cannot verify block list.” No fake live speed/rate chart.
- Usage: day/month amount and daily history; week/month bars from stored daily data; day view is total + daily comparison, **hourly chart unavailable** until hourly samples exist. Chart table equivalent and no-data gaps. Ring 4.2/5 GB (84%), label Day; switch Month.
- Quota: effective-policy trail **Samsung TV → Television → 5 GB/day + no monthly cap → Throttle 512 Kbps → Dry-run**. Null cap at matched scope wins. Show protected/exempt separately; usage still tracked. Bonus affects named period only.
- Claims: source name + stable ID fallback, trigger and captured cause, action, requested/applied/failed status, release condition. “Block wins; otherwise the lowest Kbps cap wins.” Base category/manual limit displayed below, not mislabelled as automatic claim. Primary **Release this claim**; quota Restore now suppresses that period; rule release can fire again later. Do not promise unrestricted after releasing one owner.
- Applicable rules list is a scope preview, not a guarantee that all will fire. Rules editor link, dry-run/forced flags, latest result. Schedule card initially edits a TimeWindow rule scoped to this device; per-person/holiday calendar is **New backend**.
- Protected toggle: “Never automatically throttle or block this device.” Quotas/rules honor it; current manual block bypasses it. Proposed UX refuses manual restriction on protected targets until an explicit unprotect flow; this is a deliberate safety extension requiring implementation review. Protection doesn't prevent guest/IoT network changes.
- LAN-only toggle confirmation: “All future traffic from this device will count as LAN, including real Internet traffic. Old totals and the TV’s traffic will not change.”
- Actions: Pause (**New backend**), Throttle override (**New editor**), Block (direct Web), bonus/restore (scope-bound flow), history. Error preserves edit; missing device gives MAC-based historical read-only page. Empty usage → Wait for next Agent tick; no claims → “No automatic restrictions.”

## Usage · `/usage`

Purpose: understand Internet consumption and its reliability, then predict a budget problem.

- Phone: period selector → totals/confidence → chart → ranking → table. Tablet: chart full-width, rankings in two columns. Desktop: chart 8 columns, budget/forecast 4, device table full width.
- Day/week/month segmented control with date navigation, device/category aggregation, all/selected device, Internet + LAN stacked bars (hatched LAN). Week and month derive from daily history; day shows total, not unsupported hourly data. Toggle LAN legend does not change Internet totals.
- Decimal GB everywhere after a display migration; accessible data table contains exact bytes. Old numbers were binary-scaled GB; don't rewrite history. State source coverage, age, historical unclassified data, missing samples and both-endpoint LAN counting; no billing-grade claim.
- Forecast **New query/backend**: last seven complete valid days mean Internet/day; projected exhaustion = remaining budget / mean, from today. Show method, period start/end and confidence. Need ≥3 complete days, configured ISP billing cycle, fresh usage and no unresolved counter outage; otherwise “Not enough reliable history.” Demonstration: 214/300 GB, 17.2 GB/day → about five days, around 21 Oct. No enforcement based on forecast by default.
- Primary: Export CSV (**New export**, current filters/date range, ISO period + timezone + bytes + decimal GB + confidence, escape spreadsheet formulas in names). Secondary: set budget / view device. Empty → “Usage appears after the next successful tick.” Error retains table without forecast. Offline-router suppresses live forecast and marks last read.

## Automations · hub and four sections

Phone: hub cards then local section navigation; tablet: two-column cards; desktop: four summary cards with editor side sheet. Same **choose scope → conditions → effect → review/save** pattern for every automation; advanced options disclosed, never omitted.

### Quotas

- Three levels: Default per-device, Category overrides, Device overrides. Daily and monthly numeric GB inputs are authoritative; optional sliders are shortcuts and never the only input. Presets .5/1/5/10 GB, explicit No cap, explicit Remove override to inherit. Default is not a household cap.
- Action cards: Notify only / Throttle / Block; throttle positive integer Kbps applied symmetrically. Warn chips 80/90 with ordered unique 1–99 validation and None. Exempt and Protected differ: exempt skips quota evaluation; protected allows tracking/notifying and skips automatic restriction. Dry-run on by default.
- Effective-policy preview updates draft only; include a device selector to test inheritance. Saved → Awaiting Agent message, no premature “Applied.” Protected and usage exclusion lists stay reachable from Safety and device detail.
- Primary Save quota; secondary Remove override (confirm inheritance result; Undo restores prior snapshot **new support**). Empty default → Set a safe default. Offline saves pending. Dry-run preview says “Would throttle after 5 GB/day.”

### Rules

- List: name, scope/trigger/effect summary, last firing, active count, Enabled + Dry-run controls, effective forced mode, edit/history/delete. Templates open unsaved drafts.
- Wizard steps: 1 Scope/name; 2 Trigger; 3 Actions; 4 Revert/repeat; 5 Preview/save. Back preserves draft; full review available without five forced clicks when editing. 80-character rule name.

| Existing option | Exact controls / semantics retained |
|---|---|
| Scope | All devices; one category; one or more selected devices (missing stored MAC remains visible/removable) |
| Device usage over | Day/Month, positive decimal GB within signed-64-bit bytes, max nine decimals |
| Household usage over | Same threshold; usage sums household, scope controls **action targets**, not summation |
| Time window | HH:mm start inclusive/end exclusive, distinct endpoints, ≥1 weekday; overnight belongs to starting weekday; Cairo |
| Device joined | First discovery after rule creation only; existing devices don't suddenly become new |
| Device idle | Positive days since last seen; reminder per idle episode |
| Notify | Title/body templates: `{rule}`, `{device}`, `{used}`, `{cap}`; current last two are raw bytes; preview must show actual output |
| Throttle / Block | Positive symmetric Kbps / block; protected targets skipped |
| SetWireless | Guest/IoT only, enabled on/off; show observed capability and network ID; no main choice; don't infer all model IDs exist on AX53 |
| Revert | Manual (None); period end (usage period or midnight for nonusage); AfterMinutes; WhenTriggerEnds only TimeWindow |
| Repeat | Cooldown minutes; OncePer None/Day/Month (period replaces cooldown repeat behavior); active/episode guards still apply |

- All six templates: **Bedtime throttle** 22:00–07:00 256 Kbps; **Binge alert** 10 GB/day Notify; **New device guard** Notify + 256 Kbps manual approval; **Household monthly ISP cap warning** configurable threshold Notify; **Idle device cleanup reminder** 14 days, seven-day cooldown, no delete; **Guest WiFi off at night** 01:00–06:00 restore at end. Required scope/network/threshold choices remain highlighted; template click never persists.
- Preview: scope counts, protection skips, main-WiFi restriction, forced mode, current effective limits, expected next window. **Historical “simulate last 7 days” is New backend**; daily history cannot reconstruct device joins, idle episodes, hourly thresholds, time-window effects or wireless state. Until richer event snapshots exist, show supported daily usage estimate only with explicit unavailable triggers and no false exact firing count.
- Firing history retains outcome Applied/DryRun/SkippedProtected/Failed and Active/releasing/complete; active isn't proof of success; show partial effect errors. Release requires specific firing confirmation.
- Primary Save in dry-run; secondary Save disabled draft / Cancel. Delete active rule explains next-tick release + remaining claims. Empty → Start from a template; validation errors focus field, not generic toast.

### Schedules / bedtime

Initially a curated view of TimeWindow rules (existing evaluator), no second scheduler. Bedtime template selects devices/category, weekdays, 22:00–07:00, throttle and WhenTriggerEnds. No guaranteed exact-minute execution: tick cadence applies to start and end. Person budgets, holidays, bonus time/requests, calendar exclusions and guest timed sessions are **New backend**. Empty → Set bedtime. Errors keep draft; dry-run shows future window without claiming it ran.

### Category speed policies

Show configured/unconfigured and version; download/upload Mbps independently, unlimited toggle, Save, Save all, bulk fill and force reapply retained. Warn “Not configured: existing router limit is left alone.” Reapply confirmation lists category/device count and the current direct Web behavior; long-term route through Agent single-owner gateway. Never apply a newly discovered category's default without explicit save. Empty → Discover devices. Primary Save changes; secondary Force reapply. Dry-run quotas/rules **do not** protect against base speed policy writes.

## Activity / inbox

- Phone vertical ordered timeline with filter sheet; tablet/desktop narrow filter column + timeline max 840px. Local date headings, Cairo timestamp + relative age, type icon, source, device, cause/effect/result; Why link opens detail claims/firing.
- Filters: rule/quota/device/wireless/system/notifications, live/dry-run, device, date, failure. Pagination/keyset loading required **New query** beyond latest 50. Preserve rule captured intent even after edits/deletes; do not parse log text into authoritative structured events.
- Inbox: meaningful notification events with unread count and Read action **New store**; transport attempt log separate (one event can create several delivery attempts). Dedupe/quiet suppression/delivery failure visible in delivery details. Browser push acceptance says **Sent to push service**, not Delivered to phone.
- `+1 GB today` and Restore now show named device/period, expiry/result, other claims, next-tick timing. Existing action tokens are credentials; never reveal URL/query token in Activity. Invalid/expired/already-used/not-found/rate-limited/unavailable each get a specific recovery path.
- Empty timeline → “Nothing to report yet”; empty inbox → “You’re all caught up” with View activity. Primary open attention item; secondary filters/export (**new audit export**). Offline retains read-only history; dry-run “Would have blocked”, never “Blocked”.

## Settings · `/settings/*`

Phone: index of labelled rows; detail replaces index with Back. Tablet: 200px local list + content when space permits. Desktop: 240px settings list + max 840px forms. Each page uses the universal states; Save is primary for editable forms, Retry for read-only error, Back secondary. Unsupported settings show a reason, not a broken input.

| Page | Content / action / empty or special state |
|---|---|
| Router | Provider/host/transport/user; saved password masked, blank keeps; Test separate from Save; direct test may contend with Agent session. Connection test result timestamp; unknown firmware stays Unknown. No router → Connect router. Credentials never in log/toast |
| Wireless | Guest then IoT then main; each band, state, SSID/broadcast/security/channel/width/TX and coupling. Guest edit SSID/password/enable; IoT edit SSID/enable only; capability-gated, direct command + returned readback. Main under expandable **Main WiFi · read-only** danger section. Future main writes require verified capability + extra confirmation/recovery path; keep disabled today. No snapshot → Refresh wireless. Password is a one-shot change; no persistence/display from readback |
| Notifications | Browser secure/support/permission/subscription; label; Enable / Disable here; Test all (bypasses quiet hours/dedupe); server registrations with last success/failures + Remove (not browser unsubscribe). Delivery log. Quiet hours show effective config today; editing + per-event toggles **New backend**. Explain only Warning/DryRun quota kinds are quiet-eligible, suppressed dropped; Exceeded/Applied/Reverted/SkippedProtected bypass, including dry-run Exceeded. Test doesn't prove Android display. No subscriptions → Enable on this device |
| Security | **New backend** administrator password/session management; active sessions/revoke, idle timeout and local recovery; proposed read-only family role later. Current product warning “No dashboard login”; tailnet HTTPS is not authentication. No API tokens until Agent API auth exists |
| LAN source | Configured/awaiting/OK/unavailable, last read/failure, counter verification status separate; exporter coverage IPv4 pairs only; fallback counts raw router bytes on failure, no retroactive corrections. URL/token read-only effective configuration today; future editor must encrypt/redact and coordinate both hosts. Links to counter experiment; no source → Setup guide, not fake Connect success |
| HTTPS / Install app | Browser-observed secure context and installed display mode; server TLS cannot be diagnosed solely from manifest. Install user gesture if supported, otherwise Chrome menu instructions. Push works without install. No secure context → HTTPS guide; changing origin needs resubscribe. Original HTTP access remains deployment-controlled |
| System health | **New backend** Agent heartbeat/last-success/last-duration/version, DB size/retention, error streak, provider capability health, Web version. Unknown is distinct from offline; Agent stopped alert requires external watchdog. Reboot danger dialog existing, live endpoint unverified; command sent then reconnect check, no undo |
| Backup / restore | **New backend** encrypted full bundle: coherent LiteDB snapshot + Data Protection key ring, version/schema, checksum; excludes plaintext WiFi PSKs. Config-only redacted export separate. Restore validation + maintenance lock across Agent/Web, backup current state before restore, staged restart, conflict preview. Never copy an open DB blindly; no backup → Create first backup |
| About / appearance | Version/licence (TBD until settled), provider, timezone, source/release links, System/Light/Dark, Teal/Violet audited seed, language (Arabic pending localization). Multi-router switch remains future, not fake choice |

## Onboarding and Safety Center

Onboarding one task per page: phone stacked; tablet/desktop centred max 720px with 240px step list where space permits. Back never loses draft; a completed safe step may be revisited. Setup resume state **New backend**; secrets are not draft-cached.

1. **Connect router**: provider/host/password, Test then Save; disclose direct session test. Initial admin password setup precedes router credentials once auth ships.
2. **Discover**: load saved observations or explicit direct Refresh, show elapsed time and timeout/retry; no fake success count.
3. **Categorize**: accept router categories; correct uncertain ones, protect management phone/server; naming/grouping additions gated by backend.
4. **Safe default quota**: optional 5 GB/day **per device**, Notify only, Dry-run on; preview affected and protected devices. Explicit Skip leaves router unmanaged.
5. **Notifications/install**: HTTPS/support check, permission only after Enable tap; optional install. Denied permission has Chrome instructions; Skip allowed.
6. **Done**: “Observing your network. Nothing automatic will throttle or block yet.” Home CTA; link Compare usage. No enforcement checkbox at end.

Safety Center purpose: truthful modes and controlled graduation. Phone checklist; tablet/desktop mode summary + checklist beside explanation. Primary Review one policy; secondary protected list/history. Empty policies → Create in dry-run. Offline/stale → disable graduation; dry-run remains useful.

- Separate cards for quota mix, rule forced/config flag and disabled kill switch; also “Manual controls and category policies operate separately.” Web config is reported configuration, not confirmed Agent runtime until heartbeat exposes effective settings.
- Graduation checklist: verify Internet units/reset over several days; complete T8 TV/Jellyfin + YouTube counter experiment; confirm source coverage/fallback; protect management device; review Cairo windows/actions/restores; verify Agent heartbeat and notification delivery on Android; test a single reversible throttle and release with physical router access.
- Checklist evidence records date/actor/method **New backend**, never auto-checks from HTTP OK. Cannot toggle Docker ForceDryRun in the browser; show deployment instruction, then require confirmed Agent effective config. Keep individual rules dry-run until reviewed. Do not set any live flags during design review.
- Last step: confirm **one** policy/action/target count and timing; default Throttle, not Block. “Enable enforcement for Television quota” rather than global switch. Next-tick receipt stays pending until confirmed.

## Shared interaction and content contracts

### Timing / receipts

Every enforcement-changing save/release/toggle/bonus says **“Takes effect on the Agent’s next tick (about 3 min).”** Read configured interval, localize singular/plural; not a deadline. Failed retries/action budget may require more ticks.

| Path | Feedback truth |
|---|---|
| Quota/rule save; protection; bonus/restore/release; future pause/schedule | Data saved → Awaiting Agent; requires correlated receipt/version **New backend** for Applied. Current persisted claims/firings are partial evidence, not universal acknowledgement |
| Base policy Save | Saved → next Agent tick; quotas/rules dry-run doesn't prevent base policy enforcement |
| Force reapply / Sync all | Current code attempts Web write immediately then falls back next tick; preserve this during route migration, label explicitly. Proposed later gateway unifies ownership |
| Refresh from router | Current path reconciles policies and tracks usage, so it can write; disclose before action. Wireless refresh is a direct read |
| Block/Unblock | Direct Web: sending → router command accepted / failure; recorded block, no router read-list verification. Not governed by automatic dry-run |
| Wireless edit | Direct Web; sending → returned readback / failure; allow connectivity interruption; main unsupported |
| Reboot | Direct Web, unverified endpoint; “Command sent. Checking recovery…”; no promise of reconnect time |

No optimistic green “Applied.” Pending survives route change/reconnect; unknown outcome requires reread before retry. UI-only demo's **Simulate Agent tick** is explicitly labelled and never sent to the real product.

### Confirmation, undo and forms

- Immediate metadata edits: Save + receipt; reversible removal: consequence preview + confirmation then 10-second Undo, durable Activity restore when supported. Undo is a compensating versioned change; once applied, it also waits for the Agent. Don't resurrect a deleted firing or reuse a consumed quota token.
- Block: named target + router-immediate warning; manual protected devices require unprotect first in proposed UX. Reboot: affected household + “Reboot now”; main writes future: recovery network + typed SSID only when high risk. Cancel is initially focused for dangerous dialogs. No fabricated Undo for reboot or unavailable prior state.
- Dirty sheet Escape/Back prompts Save/Discard/Keep editing; browser unload best-effort only. Show version conflict if external update occurs; never overwrite a draft from 30s polling. Busy actions idempotent server-side, not merely disabled buttons.
- Validate on blur/submit; keep submit operable to expose errors, then error summary + inline field messages. Decimal input accepts localized digits via parser while storing invariant bytes; until parser exists, explicitly state decimal point requirement. No slider-only caps, no placeholder-only labels.
- Toast success is polite and brief; persistent inline error with Retry; no generic red toast for permission denied. Skeleton region retains height and focus; no live-region flood each refresh.

### Search, keyboard, accessibility, RTL

- Global search/command dialog via app-bar Search and Ctrl/Cmd+K. Search device/category/rule/settings; commands show scope/timing then open confirmation; keyboard doesn't bypass safety. Escape closes; focus restored. `/` opens search only outside inputs/contenteditable; `?` opens shortcut help. No bare-letter destructive shortcuts.
- Semantic landmarks, skip link, one h1, actual nav links/buttons, sort buttons with aria-sort, checkbox labels, labelled charts + data table. Focus route h1 after navigation; Back restores original list position. Live statuses polite; actionable error summary assertive once. At 320px / 200–400% zoom no page overflow; usable with TalkBack, NVDA/VoiceOver and touch exploration. Test keyboard-only and high contrast modes.
- Arabic: `dir=rtl`, logical CSS, mirror directional arrows, preserve chart time progression with explicit axis order; MAC/IP, GB/Kbps and entered IDs use bdi. Font fallback Arabic-capable; no forced letter spacing. Test 40% longer translations, localized numerals, plural forms zero/one/two/few/many/other, Cairo DST. Prototype RTL toggle is layout readiness, **not translated Arabic**.
- Localization catalogue groups: nav/route titles; device/category names/fallbacks; medium/online ages; all modes/statuses/receipts; policy scope/actions/reverts; trigger/template descriptions; units/cap labels; validation/errors/empty/loading; quota warnings/bonus/restore; quiet hours/permission/help; security/session/recovery; time/date/forecast; destructive dialogs/undo; keyboard/search/install/offline; notification payload/action titles. User-created strings remain user content. Never concatenate English sentence fragments.

### PWA / offline

- Current SW handles push/actions only; proposed offline shell is **New implementation** on trusted HTTPS, caching versioned non-sensitive shell assets only. Blazor Server interaction requires a circuit; offline shell says “Connect to manage your network”, shows no pretend live controls, and doesn't queue restrictions/secrets. Never cache auth pages, tokens or household API responses.
- Install prompt only after meaningful use and explicit tap; Android manual-menu fallback. Add maskable local icons and theme-color light/dark. Manifest shortcuts Home, Devices, Usage, **Pause TV** (opens confirmation, never executes from GET). Dynamic TV target needs a configured favourite, not hardcoded MAC in manifest.
- Badge count is durable unread inbox count **New backend**, feature-detected, no critical information conveyed only by badge. Push action buttons +1 GB / Restore now stay scoped/expiring/single-use; action result opens relevant device/period; mobile OS may omit actions, so body tap provides equivalent in-app path. Test secure phone access, DND and background behavior separately.
