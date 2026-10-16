# 02 · Information architecture

Design proposal · 16 October 2026 · routes below are future Blazor routes, not claims about today's app.

## Navigation decision

Five primary destinations: **Home · Devices · Usage · Automations · Activity**. Settings is a labelled 48px app-bar control at every size and a utility link in rail/drawer. A persistent Safety/Mode strip links to the Safety Center. Six equal primary destinations would overload the phone bar; settings is less frequent than daily monitoring.

| Viewport | Navigation | Content |
|---|---|---|
| ≤600px | Fixed 80px bottom bar + safe-area inset, five labelled destinations | 16px gutters; one column; app bar 64px; Settings and Search in app bar |
| >600px and <1200px | 88px leading rail, five destinations + Settings utility | 24px gutters; two columns where each card has ≥280px; no bottom bar |
| ≥1200px | 248px persistent leading drawer, five destinations + Settings and Safety utilities | 32px gutters; max content 1360px; 12-column grid |

At exactly 600px use the bottom bar; at exactly 1200px use the drawer. At 200% zoom use the effective CSS viewport. Selected destination has a tonal pill, icon and `aria-current="page"`. Nested pages keep their parent selected. The app bar supplies a breadcrumb/back link on detail pages. Preserve scroll and filters when returning to a list.

## Full sitemap / route contracts

| Area | Routable pages (`@page`) | Details |
|---|---|---|
| Home | `/` | Summary, attention queue, restrictions, quick actions |
| Devices | `/devices`; `/devices/{Mac}`; `/devices/{Mac}/history` | Canonical uppercase dash MAC e.g. `02-00-00-00-00-02`; validate parameter; unknown MAC gets a useful not-found page |
| People **NEW** | `/devices/people`; `/devices/people/{PersonId:guid}` | Group devices; only offered once backend ships |
| Usage | `/usage`; `/usage/devices/{Mac}` | Query `?period=day&date=2026-10-16&group=device`; week aggregates daily history; never invent hourly samples |
| Automations | `/automations`; `/automations/quotas`; `/automations/rules`; `/automations/schedules`; `/automations/policies` | Hub + four subpages; Schedules initially projects existing TimeWindow rules |
| Quota editors | `/automations/quotas/default`; `/automations/quotas/category/{CategoryKey}`; `/automations/quotas/device/{Mac}` | Route-backed sheet over list; standalone full editor on direct load; category keys URL-encoded, data-driven |
| Rule editors | `/automations/rules/new`; `/automations/rules/{RuleId:guid}/edit`; `/automations/rules/templates`; `/automations/rules/{RuleId:guid}/history` | Query `?step=trigger`; Back closes sheet only if entered from list, otherwise navigates to Rules |
| Activity | `/activity`; `/activity/inbox`; `/activity/events/{EventId}` **NEW ID** | Query `?kind=quota&mac=…&mode=dry-run`; claim/firing links use stable persisted IDs; older activity entries have no stable event ID |
| Settings | `/settings`; `/settings/router`; `/settings/wireless`; `/settings/wireless/{NetworkId}` | Provider + transport, test connection; network capability gates |
| Settings continued | `/settings/notifications`; `/settings/security`; `/settings/traffic`; `/settings/app`; `/settings/health`; `/settings/backup`; `/settings/about` | Quiet hours, auth, LAN source, HTTPS/install, heartbeat, backup; proposed controls gated until implemented |
| Safety | `/settings/safety`; `/settings/safety/graduate` | Effective per-feature modes; guided enforcement review |
| Setup | `/setup/{Step:int}` | 1 Connect → 2 Discover → 3 Categorize → 4 Quota → 5 Notifications/install → 6 Done; resumable |
| Auth **NEW** | `/login`; `/setup/admin`; `/settings/security/sessions` | Server-authenticated cookie session; safe return URL; no public router endpoint |

Use ordinary Blazor Router pages and `NavigationManager`, not a SPA nested inside Home. Query parameters via `SupplyParameterFromQuery`. Reserve literal `/devices/people` ahead of MAC lookup. The static prototype uses `#/…` equivalents for file:// navigation; those hashes are not production routes.

## Old → new mapping and complete retention inventory

**E** = existing implementation; **N** = new UX/backend; **V** = live verification still required. Inventory based on Home.razor, all Tabs components, manifest and listed planning docs; dated status takes precedence over older plan prose.

| Existing location | Features retained | New destination / notes |
|---|---|---|
| Connection card | Provider, host, username (currently hidden input), router transport HTTP/HTTPS, password keep-if-blank/encrypted storage, Save, Test, Cancel, test errors | Settings → Router + setup. Router transport is separate from dashboard HTTPS. Config presence does not prove connected. Model/firmware already use a confirmed read, with unknown fallback for missing responses. E/V |
| Devices | Online-only toggle; category filter; name/MAC search; grouping; hostname/friendly name; MAC/IP; medium; last seen; effective limit; managed/unmanaged; router-reported limit/drift; Internet today/month; Block confirmation/Unblock + errors | Devices list/detail. E. Per-device override exists in domain but no complete edit UI in current Home; rename/category editing also new UI with persistence semantics to design. Block state is recorded intent, not a fresh router block-list read. |
| Device type policies | Enabled, separate download/upload Mbps (stored Kbps), version, Not applied guard; bulk apply draft; Save all; per-category Save; per-category force reapply and Sync all | Automations → Speed policies. Retain all categories, including newly reported strings. Distinguish draft bulk fill from Save. Existing force paths write immediately from Web. E/V |
| QuotasTab / editor | Default/category/device scopes, add/remove override, daily/monthly decimal GB, NotifyOnly/Throttle/Block, positive symmetric throttle Kbps, ordered unique warn % 1–99 or none, exempt, dry-run, progress + Cairo period, protect/unprotect devices | Automations → Quotas; device detail effective preview; Safety protected list. Blank cap is explicit **No cap**, not inheritance. Delete override to inherit. Device → category → default; matched policy supplies both periods, no field-by-field merging. E |
| UsageExclusions | LAN-only flag; future bytes only; excludes all Internet use too | Device detail → Usage settings + Settings → LAN source. Does not subtract TV traffic automatically. E/V |
| RulesTab / RuleEditor | Create/edit, Enabled/DryRun toggles, confirmed delete; five triggers; scope All/Category/Device list; multiple actions; four actions; four revert choices; cooldown + OncePer; validation; forced-dry-run banner + kill switch | Automations → Rules. Exact options and templates in screen spec. Main WiFi forbidden. Network list currently model IDs, not capabilities; unavailable IDs can fail. E/V |
| RuleFiringHistory | Rule/device/mode filters; local time; saved name/subject/affected networks; outcome/status/actions/revert/error; captured reason; active claims lookup; Release now confirmation + release requested | Rules history + Activity + Device → Why restricted. Block wins, else lowest Kbps. Release one claim leaves others. E/V |
| NotificationsTab | Browser secure/support/permission/subscription state; optional label; enable, disable here, remove server registration, test all + per-subscription result; created/last-success/failures; latest 20 log attempts; effective quiet hours/VAPID/dedupe/action lifetime | Settings → Notifications; delivery log in Activity. Existing quiet hours are configuration, read-only in UI. No per-event switches or durable unread inbox today. E/V |
| Push / service worker | `+1 GB today` / `+1 GB this month`; Restore now; single-use expiring scoped tokens; action errors/retry; root landing; manifest install metadata/icons | Inbox/action-result page + device deep links **NEW navigation**. Bonus named period only; restore suppresses that quota claim for the period, other claims remain. E/V |
| Activity log | Last 50 entries with event type/time/message/MAC; policies, device transitions, blocks, quotas, rules, LAN reset/outage | Activity timeline. Existing generic times are UTC formatted without timezone conversion; normalize to Cairo. Filtering, paging, unread and stable audit IDs are N. |
| Usage | Day/month selector + available periods; one/all device; Internet total; LAN excluded; category ranking; device totals; missing-device MAC fallback; current state vs history | Usage + device charts. Existing formatter divides by 1024 but labels GB; migrate display explicitly, do not rewrite bytes. LAN total counts both endpoints. E/V |
| Management | Reboot confirmation, unavailable without connection, progress, command-sent/failure result + log | Settings → System health → Danger zone. Endpoint unverified live; command accepted ≠ recovered. E/V |
| Wireless | Refresh; kind/band; enabled, SSID/broadcast, security, channel/width/TX, coupled-radio explanation; guest SSID/password/on-off, IoT SSID/on-off, blank password preserves; cancel/errors | Settings → Wireless. Main is read-only; IoT password, all channel/width/TX writes unavailable. Extra confirmation cannot enable unsupported main writes. E/V |
| LocalTrafficStatusLine | Not configured / awaiting first read / OK + time / unavailable since | Persistent usage confidence + Settings → LAN source. Never show “LAN excluded reliably” just because HTTP reads succeed. E/V |
| Layout / PWA | Blazor error UI, reconnect/resume/retry, manifest standalone/start URL/icons, push worker with **no page cache** | Shell reconnect banner + App settings. Offline shell, shortcuts, badging, refreshed icons are N. |

## Feature growth rules

- No empty “coming soon” nav destinations in production. Use explanatory cards within their parent until the backend exists; prototype demonstrates future states with explicit labels.
- Household cap is an **ISP budget** distinct from default **per-device** quota. Today's HouseholdUsageOver rule offers an alert threshold, not a household quota model.
- A second router needs router-scoped identities and stores first. Show “Home network · Archer AX53” in the shell now; don't add a working router switcher until attribution exists.
