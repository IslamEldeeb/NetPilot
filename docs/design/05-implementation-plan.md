# 05 · Implementation roadmap

Design only. Each row is a separately reviewable Codex task; no commits are authorized by this package. Keep .NET 10, LiteDB, two deployables and IRouterProvider boundaries. S/M/L follow [gap analysis](04-gap-analysis.md). **UI** uses current backend; **BE** requires new backend work; **UI+BE** cannot ship its mock controls as working features.

## Migration / rollback contract

- Add `NetPilot:Ui:RedesignEnabled` (default false) and `/legacy` containing today's Home unchanged in behavior. Flag routing at `/`, not data model selection. No duplicated router mutation handler logic. Extract facades as one step, reuse from both shells.
- Land read-only pages first; old tabs remain reachable until feature parity inventory in [02](02-information-architecture.md) is signed off. Redirect tab intents to explicit routes with preserved query filters; existing push `/` links continue to work. Route-backed sheets support direct load/back/refresh.
- New collections/fields additive with compatible defaults; theme/local preferences optional. Don't rewrite raw usage or old policy IDs. No new NuGet packages in foundation tasks; dependencies need separate review.
- Toggle flag off or deploy previous Web image to roll back visual changes; Agent/database remain compatible. **Authentication enforcement must remain outside the UI flag**—rollback must not expose unauthenticated legacy pages. Backend tasks need separate reversible rollout, backups and compatibility gates; do not promise an old binary can read incompatible data.
- Every stage shippable: unsupported future items hidden/explained, all existing mutations still available via legacy, no “Applied” without receipt evidence, quotas/rules remain dry-run until live verification.

## Foundation and safety (ordered)

| Task / kind / effort | Scope + likely files (future changes) | Risk / dependency | Acceptance and tests |
|---|---|---|---|
| **T9a UI S** Tokens/theme | Add `wwwroot/css/tokens.css`, theme JS; App.razor loads before paint; appearance setting | Legacy CSS cascade / flash; none | System/light/dark + two seeds; no network font dependency; role-pair contrast script, storage-denied fallback, reduced motion. Use this package's tokens |
| **T9b UI M** Primitives | Components/DesignSystem Button, Chip, Meter, Field, Sheet, Dialog, Receipt shell, EmptyState, Snackbar | Native dialog/focus and Blazor disconnect | 48px hit areas, focus trap/restore, dirty draft confirmation, labelled controls; HtmlRenderer semantics + keyboard browser smoke; receipt never fake-completes |
| **T10a UI M** Shell/nav | MainLayout, NavMenu, Routes, new AppBar/ModeStrip; feature flag + LegacyHome | Mobile sticky overlap, exact breakpoints | Five bottom items ≤600, rail 601–1199, drawer ≥1200; Settings everywhere; /legacy parity. 390/768/1280 and 320/zoom reflow screenshots |
| **T10b UI M** Route extraction | Pages/Devices, Usage, Activity; Settings/Router/Wireless/Health; Automations pages mount existing Tabs | Losing handler behavior; depends T10a | All nine-tab inventory retained; deep-link/Back/refresh/error routes; no changed router timing. Snapshot parity + manual interaction checklist |
| **T10c UI+BE M** Feature facades | Web/Features/* query/command adapters, scoped snapshots, cancellation/version fields; Home shrinks to composition | Shared DB races, polling overwrites drafts | No giant root service. Query only current screen, bounded history; cancel on dispose, refresh doesn't overwrite drafts. Core/Data command conflict and paging tests; two-circuit smoke |
| **T11a BE M** Admin credential/session | Core/Data admin credentials + session version; Web Program cookie auth, login/setup pages, antiforgery | G01, security review; HTTPS required for normal session | Password hashing, throttled failures, local recovery command documented, no secrets in logs. First setup local one-time claim; no takeover race. HTTP/auth/CSRF/session expiry/revoke tests; no PIN-only default |
| **T11b UI+BE S** Apply auth everywhere | Gate redesign/legacy routes + command endpoints; session UX, safe return paths | T11a; push tokens must remain scoped | Unauthenticated requests can't mutate/read household data, logout/revoke closes active circuit; exact scope action tokens keep intended action-only access. Browser login/expired circuit/push action regression |
| **T12a BE M** Agent health | Core health record; Data store; Worker persists tick start/end/last successful read/duration/error streak/effective mode | Writing heartbeat falsely on failed tick | Distinguish Web alive, Agent alive, router fresh; expose no credentials. Fake clock/failure/restart tests; shared-process store read verification |
| **T12b BE M** Operation receipts | Command/request IDs + expected version and per-target acknowledgements in Agent stores | T10c/T12a; partial actions/retries | Saved/Pending/Applied/Partial/Failed survive restart; duplicate submit safe; don't equate active claim with confirmed router effect. Fault injection + concurrent writes tests |
| **T12c UI S** Wire feedback | ModeStrip/Receipt/Home/Health use T12 records, stale thresholds based on configured cadence | Agent/Web config divergence | Unknown before load; warn after >2 missed expected intervals + grace; no deadline countdown. Render all states; stop-Agent deployment check outside sandbox |
| **T13a BE M** Session ownership | Phase3 RouterSessionManager in Agent; minimal authenticated internal command/read endpoints | Planned architecture; network listener/auth/deploy requires separate review | One serialized router login owner; command IDs and failures propagate. Fake provider concurrent-client test; keep separate processes |
| **T13b UI+BE M** Migrate direct operations | Web facades for test, refresh, block, wireless, reboot, force reapply call gateway; Agent endpoint auth | T13a; current direct action parity | Remove Web router login ownership only after parity verified; retry without duplicate mutation; actual capabilities surfaced. Fake HTTP/provider tests; no live reboot during automated smoke |

Authentication and heartbeat may be developed before the optional shell rollout. T13 is the deliberate later ownership change; T10 must not silently rewrite current behavior to match a future architecture.

## Daily-use screens

| Task / kind / effort | Scope + files | Risk / dependency | Acceptance and tests |
|---|---|---|---|
| **T14a UI M** Devices | DevicesPage, DeviceCard/Row, filters, bounded query | T10; arbitrary categories/missing MAC | Search/filter/group Category/None, selection counts, desired vs reported, offline, protected labels, direct action receipts. Renderer + browser filter/keyboard/detail navigation |
| **T14b UI+BE M** Identity | Local name/category/icon ownership fields and update command + detail editor | Router discovery overwrite, MAC randomization | Stored local override survives Agent snapshot; clear restores reported values; missing devices remain readable. Data round-trip/reconciliation conflict tests |
| **T14c UI M** Device detail | DevicePage, ClaimStack, effective quota preview, usage/history/rules panels | T12 receipts for Applied; T14a | Winning claim + all owners/base policy; claim-specific release/restore timing; protected/LAN-only confirmation. Existing resolver/claims tests + browser overlap/release/error scenario |
| **T15a UI+BE M** Usage queries/charts | UsagePage, daily aggregation projection, shared formatting + CSV handler | G04; unclassified history, endpoints counted twice | Decimal GB consistently; day total/week/month daily bars; exact-byte accessible table + escaped CSV; no invented hourly data. Query/date/missing sample/CSV tests |
| **T15b BE S** ISP budget | New budget store: decimal bytes, billing day, timezone; separate cycle aggregation | Not replacing calendar quota periods | 28/29/30/31 and Cairo DST tested; explicit household budget distinct from per-device default; history unchanged |
| **T15c UI+BE S** Forecast | Forecast query + budget editor/summary | T15a/b, trusted counters | Seven-day method, ≥3 valid days, outage suppression and transparent estimate. Deterministic dates/data tests; 214/300 @17.2/day rounds to ~21 Oct demo |
| **T16a UI M** Home read-only | HomeDashboard, health summary, top consumers, attention/restrictions cards | T12/14/15; no unknown-as-zero | Totals tie to Usage; attention links scoped; no household cap shown until configured. Renderer all states + phone viewport smoke |
| **T16b BE M** Temporary actions | Expiring pause/throttle claims and scoped dashboard bonus command | T12/13, protected + overlapping owners | Restart-safe timed release; category count/protected skip; no base policy corruption; all holds accounted for. Claims concurrency/expiry/restart tests |
| **T16c UI S** Quick actions | Shared action sheets on Home/device; favourites | T16b | Target/duration/next-tick preview; bonus named Day/Month; no arbitrary token repurposing. Browser submit/retry/release; no automatic router calls in test |

## Automations, activity and settings

| Task / kind / effort | Scope + files | Risk / dependency | Acceptance and tests |
|---|---|---|---|
| **T17a UI M** Quotas | AutomationsHub/QuotaPage/QuotaSheet; reuse validation/resolver | Blank-vs-inherit confusion | All scope/action/warning/protected/exempt/dry-run behaviors retained; effective preview; save/remove/undo if supported. Resolver and renderer edge cases + keyboard numeric input |
| **T17b UI M** Rules list/templates | RulesPage; reuse RulesTab codebehind/TemplateCatalog | Unsupported network choices | Six unsaved templates, enabled/dryrun flags, forced mode + kill switch, history. Core presentation tests + template browser smoke |
| **T17c UI M** Rule editor | RuleWizard/steps/share Sheet; reuse RuleForm | Five triggers/four actions/four revert options | Name/scope/weekday/multi-action/OncePer/cooldown preserved, raw-byte notification preview honest; step Back preserves draft. Existing RuleForm tests + invalid wizard browser flow |
| **T17d UI S** Schedules/policies | TimeWindow filtered view; category speed policy page | Second scheduler or hidden force writes | Schedule maps to same RuleId; all policy bulk/save/reapply/version guards retained. DST/overnight core tests + old/new parity checklist |
| **T17e UI+BE M** Preview/portability | New preview service + versioned rules JSON mapping/import | No historical snapshots for exact replay | Current-state preview no writes; only supported coarse daily estimates labelled; invalid ID/import dry-run default. Schema/validation/fake no-write tests. Full replay separate later task |
| **T18a BE M** Audit/inbox store | Structured event IDs/actor/source/correlation; durable unread notifications | G15/G14, privacy/retention | Compatible old event rendering; paging; dedupe event vs attempts; no secrets; active-state retention preserved. Store concurrency/paging/prune tests |
| **T18b UI M** Timeline/inbox | ActivityPage/Inbox, filter sheet, Why links, unread badge | T18a | Rule/quota/system/notifications, dry-run and failed filters; token results explained; read state survives reload. Browser filter/deep-link/mark-read + screen reader check |
| **T18c UI+BE M** Undo | Versioned before/after config snapshots and compensating command | T12/18a, conflicts | 10s Undo plus durable recovery; no Undo for reboot or consumed action token. Conflict/retry/expired undo tests; announce queued compensation |
| **T19a UI M** Router/wireless | Settings index/forms; current capability-driven operations | Live protocol unknowns | All current fields/actions; guest/IoT first; main read-only danger disclosure; password blank keeps; offline disabled direct paths. Fake snapshots + browser confirmation; real captures separate |
| **T19b UI+BE M** Notifications | Shared preference store + editor + per-event toggles, current browser/registration controls | Two-host config consistency; suppression contract | Apply same settings to Agent/Web; show bypass rules, test-all acceptance vs display, Remove vs Disable distinct. Dispatcher tests + real Android HTTPS matrix |
| **T19c UI S** Health/traffic/app/About | Effective status cards, setup guidance, unknown fields | T12, T8 and HTTPS unverified | No token echo; configured/healthy/verified distinct; no fake firmware/CPU. Renderer capability/stale/unsupported states |
| **T20a BE M** Backup/export | Coherent snapshot + key-ring encrypted bundle, redacted config export | G06; coordinate two writers | Validate format/version/checksum; failure leaves existing DB intact; protected output. Snapshot integration tests and restore rehearsal on copied isolated volume |
| **T20b UI+BE M** Restore/retention | Backup UI, maintenance/restart handshake, retention policy settings | T20a/18a | Preview, safety copy, restore progress, mismatch recovery; cannot prune active claims. Manual recovery drill + failure injection |

## Onboarding and later product extensions

| Task / kind / effort | Scope + files | Risk / dependency | Acceptance and tests |
|---|---|---|---|
| **T21a UI+BE M** Setup | Setup pages/resume store + auth first setup | T11/14/17/19; no plaintext drafts | Six-step flow, Skip, retry, resume; quota remains dry-run; browser denied/insecure push states; saved secret not echoed |
| **T21b UI+BE M** Safety Center | Mode summary, evidence checklist/graduation records | T12; T8 live check still blocking | Forced config not changed by UI; graduate one policy, protected list + release rehearsal; no false “verified” from source HTTP. Permission/mode/conflict tests |
| **T22a BE M** People | New person/assignment fields + scopes, MAC identity review | T14b; household privacy | Stable PersonId, multiple devices, no automatic identity merge; resolver/scope membership tests |
| **T22b UI+BE M** Family/schedules | Read-only role + person view and timetable exceptions | T11/T22a, privacy | Server authorizes every read/write; readable family view, no admin credentials shared; role and Cairo calendar tests |
| **T22c UI+BE M** Requests/approvals | Kid request + admin queue, durable approved devices | T16b/18a/22b | Expiring/rate-limited/actor-audited, no request token leaks; approve/deny/duplicate browser flow |
| **T23a UI M** RTL/a11y/performance | Resources, logical CSS, Arabic translation review, bounded circuit payloads | Should be considered from T9, final audit here | All strings catalogued; TalkBack/keyboard/high-contrast, 320px/400% zoom, Arabic 40% expansion; representative 250-device fixture; no full-history poll per circuit |
| **T23b UI+BE M** PWA | Offline shell, maskable icons, shortcuts, unread badges | HTTPS, T18 inbox; not offline Blazor | Offline gives no active controls/data write queue; app update/reconnect doesn't lose draft; shortcuts only open confirmation; Android install/push/actions test |
| **T24a BE M** Integration contract | Phase4 Agent REST/capability/availability tokens + OpenAPI | T13, auth and stable IDs | No MQTT rewrite; versioned idempotent endpoints, negative auth/failure tests; HA client separate task |
| **T24b UI+BE M** Optional probes | ISP test history + provider health data when verified | New probe + capture review | Cost disclosure, unsupported fields hidden, no auto firmware update; fake probe/timeout tests |
| **T24c UI S** Probe presentation | Health/speed-test trend cards over T24b readings | T24b; verified capability fields only | No invented CPU/uptime; test timeout/missing samples; opt-in cost and sample source visible |
| **T25a Design S** Multi-router preparation | Draft router-scoped identity/schema compatibility ADR and first vertical-slice tickets | A second real router/provider; G29 | Review stable IDs, legacy data upgrade and route attribution before any implementation; do not add a working switcher yet |
| **T25b Design S** DNS integration boundary | Draft Pi-hole/AdGuard capability/auth contract and a small read-only integration slice | G28; filtering scope decision | Specify unsupported features and privacy; no claim that router throttling filters content; implementation separately reviewed |
| **T25c Design S** Explainable suggestions | Draft suggestion/confirmation/dry-run interaction and data contract | G30; verified usage and explicit consent | Suggestions never silently enforce; define testable evidence and dismissal; implementation separately reviewed |

## Proposed Blazor decomposition and state ownership

```text
MainLayout → AppBar + AdaptiveNavigation + ModeStrip + ReconnectBanner
Pages/HomeDashboard → HealthSummary + BudgetCard + AttentionList + RestrictionList
Pages/Devices → DeviceFilters + DeviceTable / DeviceCards + BulkActionBar
Pages/DeviceDetail → Identity + UsageChart + QuotaMeter + ClaimStack + DeviceHistory
Pages/Automations/* → QuotaEditor / RuleWizard / TimeWindowSchedule / SpeedPolicyEditor
Pages/Activity → EventFilters + Timeline / Inbox
Pages/Settings/* and Setup/* → shared FormField + EditorSheet + ActionReceipt
```

- Feature-scoped Web `DeviceQuery`, `UsageQueryFacade`, `AutomationCommands` and `HealthQuery` adapt existing store contracts; proposed names, not existing APIs. Keep Core resolvers/forms pure and router-agnostic. No router SDK in page components.
- Circuit-scoped feature state holds immutable read snapshots and separate mutable edit drafts. Route/query is authoritative for selected device/period/filters. Don't put admin/session/draft state in process singletons. Browser storage contains appearance only, never credentials or claims.
- One cancellable refresh loop per active feature (initially existing 30s cadence), paused when hidden where practical; query current periods and bounded history, not every history row per circuit. Shared LiteDB is the cross-process truth; an in-process Web event does not prove the separate Agent changed anything.
- Commands return a saved intent version/request ID, invalidate relevant projections, then await durable Agent acknowledgement. Per-target partial failures remain visible. Refresh never replaces a dirty draft; compare expected version on Save and offer reload/review conflict. Reject stale mutation attempts server-side.
- Use stable `@key` IDs, paged history/device tables, local filter debounce and section ErrorBoundary; cancel outstanding work on navigation/dispose. Virtualization follows a measured large-household need and must preserve keyboard/table semantics.
- Login/logout use ordinary HTTP POST or static SSR endpoints to issue/clear cookies, then establish a new circuit. Revalidate session version/expiry within the active circuit and authorize command services; hiding a component is insufficient. See [Microsoft Blazor authentication](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0), [cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) and [state management](https://learn.microsoft.com/en-us/aspnet/core/blazor/state-management/?view=aspnetcore-10.0). No EF/Identity database migration is assumed for the single-admin LiteDB design.

## Test and release gates

- **Current environment evidence:** no bUnit or Microsoft.Playwright NuGet packages in `/Users/ieldeeb/.nuget/packages`; no `playwright`, `puppeteer` or `@playwright/test` resolvable from project Node. Chrome application exists, but sandbox headless launch must be verified separately. No package added for design.
- Existing approach: xUnit Core presentation/form logic and Data concurrency tests; prior T7c isolated `HtmlRenderer` checks (work-state evidence) without a Web package. Add meaningful pure view-model/format tests there where appropriate; static render checks verify semantics, not browser behavior.
- Proposed browser smoke, provisioned separately: Playwright with fake read/command fixtures, no real router. Journeys: setup skip/retry; login/revoke; phone nav; device find/detail; quota inheritance; every rule template + invalid form; claim release with another owner; CSV; subscribe support states; reconnect; dangerous action cancel; no network writes from dry-run preview. Screenshots 390/768/1280 both themes; manual TalkBack/keyboard and Arabic review.
- Run relevant tests per task; normal solution gate outside known socket/native-crypto sandbox restrictions. Don't claim past fallback test runs certify the new UI. No need to run production solution tests for this docs-only package.
- Live rollout checklist remains independent: original counter experiment, router comparison, next-tick/receipt/overlap restoration, Android HTTPS/install/push/action confirmation, gateway session contention and backup recovery on copied volumes. Keep `NETPILOT_RULES_FORCE_DRYRUN=true` and quotas dry-run until counters and safety are verified.
