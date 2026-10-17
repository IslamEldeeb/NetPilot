# NetPilot · A calmer home network

UX/UI redesign package · 16 October 2026 · **design reference and staged implementation**. The original prototype is a local simulation; see implementation status below. Nothing here verifies the real router, exporter or Android delivery, or authorizes enforcement.

## Implementation status

- **Batch A1 + M1 implemented:** MudBlazor 9.11.0 shell/primitives and early System/Light/Dark theme, adaptive navigation/search, configuration-based mode strip, default-on `NetPilot:Ui:RedesignEnabled`, and shared decimal SI byte formatting. Set `NetPilot__Ui__RedesignEnabled=false` directly or `NETPILOT_UI_REDESIGN=false` through Docker Compose to restore the classic root dashboard.
- **Batch B implemented:** `/devices` now has responsive MudBlazor cards/table, saved-data polling, filters/sort/grouping, selection and confirmed bulk block/unblock. `/devices/{Mac}` provides canonical MAC routing, local identity overrides, Internet/LAN totals, 30 daily bars with exact values and honest gaps, effective quota previews, automatic claim/firing release requests, protection/LAN-only flags, matching rules and bounded MAC-filtered activity. Agent health comes from the heartbeat; tick-based apply labels are not per-router acknowledgements. Sign out is an antiforgery-protected POST when login is enabled.
- Identity fields are additive LiteDB fields. `UpdateIdentityAsync` changes only local identity in a transaction; snapshot upserts merge the persisted identity so stale Agent writes cannot erase edits or resurrect cleared overrides. Older friendly names are retained. Clear overrides returns to hostname/router category/icon defaults.
- Legacy Devices parity: refresh and manual block/unblock use the existing provider/reconciliation/usage paths through `DeviceRouterActions`; desired/manual/router limits and drift remain visible on list/detail. The Web polls stores every 30 seconds; only explicit refresh contacts the router (and may apply configured speed policies). Protected devices remain subject to manual blocks. All nine tabs remain at `/legacy`, unchanged.
- Links such as `/legacy#usage`, `/legacy#policies` and `/legacy#activity` select the matching tab. Routes still awaiting extraction explain migration status; Automations embeds the existing quota/rule components, and Settings now has dedicated MudBlazor sections. Usage and Activity still link to their classic screens; Devices has its own screens and a Classic escape link.
- **Batch D2 implemented:** `/settings` now has MudBlazor Router, Wireless, Notifications, Appearance, Health/Traffic/App/About, Backup/retention and sign-out sections, with Budget and setup links. Guest/IoT operations retain capability gates; main WiFi stays read-only. Legacy screens remain unchanged. Shared notification preferences are read by Agent/Web at dispatch time; test-all bypasses suppression. Retention preserves active claims/actions/firings and pending quota restoration metadata.
- `/setup` resumes six steps with Skip/retry. First interactive root visit without a router opens setup unless completed/skipped. Only progress and completed settings persist; no plaintext secret drafts. The optional default quota is create-only, NotifyOnly and DryRun. `/settings/safety` shows configuration versus Agent-reported modes, protected devices and dated administrator attestations. Per-policy graduation requires fresh Agent/router evidence, checklist, release rehearsal tied to policy version, explicit confirmation and an atomic policy fingerprint check plus durable record. Neither forced-dry-run configuration nor Docker defaults change.
- Backup uses an antiforgery-protected POST; restore stays manual under D8. Language remains a placeholder; Budget links to Batch C’s `/usage/budget`. Release rehearsal requires real device/router observation, never inferred from HTTP success. Static render and Core/Data tests cover local behavior; real mobile interaction, router/reboot/recovery, counters/T8, Android/HTTPS and restore drills remain unverified.
- **A2, C, D1 and E:** tracked separately; no completion claimed here. Batch B drives PendingChip from heartbeat and locally recorded save times; existing release requests without a saved request timestamp remain pending, never inferred applied. The mode strip reports saved policies and Web configuration, not verified Agent runtime state; mixed/unknown states remain explicit.
- Batch B build, rendering/persistence tests and Compose validation are covered by its delivery report. Real mobile/browser interaction, router actions, Agent release timing and phone delivery still need live checks. The responsive split is exactly ≤600px; app-shell drawer/bottom-navigation overrides are unchanged. Activity shows up to 20 matching events from the latest 500 network entries, not an unlimited device audit.
- Validation and remaining browser checks are recorded in the A1 delivery report. Prototype-only features below remain proposals unless explicitly listed above.

## Open and review

Open [prototype/index.html](prototype/index.html) directly in Chrome/Edge/Safari from the filesystem; no server, build, CDN, external images, font requests or production credentials. Keep the folder structure intact. All data is fictional; the prototype makes **no network calls**. The clock is fixed at 16 Oct 2026, 21:42, Africa/Cairo.

Suggested review path (10 minutes):

1. Home → Samsung TV → review manual limit / dry-run preview.
2. Footer **Enforcing example** → device → review overlapping claims → Release → **Simulate Agent tick**. This is not a rollout recommendation.
3. Devices → search/filter/select → Pause. Check target/duration/protection copy.
4. Usage → day/week/month → chart data table → export fictional CSV.
5. Automations → Quotas → Edit; Rules → template → five-step wizard; Schedules; Speed policies.
6. Activity → Inbox → scoped quota-action copy; Settings → Notifications / Safety Center / Wireless.
7. Footer **Try onboarding** → all six steps. Try Dark, Violet, RTL and offline/loading/empty/error scenarios.
8. Resize to **390px, 768px, 1280px**; test keyboard Tab/Escape and Ctrl/Cmd+K search. Native dialogs need a modern browser. Reset with reload; only theme persists locally.

## Package index

| File | Review focus |
|---|---|
| [01-design-system.md](01-design-system.md) | M3 roles, component comparison/choice, tokens, type, motion, component contracts, accessibility |
| [02-information-architecture.md](02-information-architecture.md) | Adaptive nav, full sitemap/routes, exhaustive nine-tab feature mapping and status |
| [03-screens.md](03-screens.md) | Responsive layouts, content/actions/states, exact rule options, safety/onboarding, interaction/i18n/PWA contracts |
| [04-gap-analysis.md](04-gap-analysis.md) | 30 ranked gaps with rationale/effort/dependency/current component; ten main findings |
| [05-implementation-plan.md](05-implementation-plan.md) | T9a onward small tasks, UI/backend flags, files, risks, acceptance/tests, migration/rollback |
| [tokens.css](tokens.css) | Ready-to-paste light/dark/semantic roles + spacing/shape/type/elevation/motion + violet option |
| [prototype/index.html](prototype/index.html) | File-based entry; native HTML, hash navigation |
| [prototype/prototype.css](prototype/prototype.css) | Responsive layout and components using shared tokens |
| [prototype/app.js](prototype/app.js) | Demo data, screen composition and local interactions only |
| [prototype/verify.mjs](prototype/verify.mjs) | Dependency-free static/runtime stub checks and contrast calculations; not a browser test |

## Decisions to confirm before implementation

| Decision | Proposed choice / review boundary |
|---|---|
| Components | MudBlazor 9.11.0 (MIT), system-font NetPilotTheme and a few domain wrappers. Replaces the native primitive set under amended D1. Classic view keeps Bootstrap; prototype remains historical reference |
| Navigation | Five daily destinations Home/Devices/Usage/Automations/Activity; labelled Settings in app bar, plus rail/drawer utility; Safety/Mode persistent. Bottom ≤600, rail 601–1199, drawer ≥1200 |
| Authentication | One administrator password + server cookie session, rate limits/recovery; optional read-only family view later. PIN alone and tailnet access do not protect dashboard sessions |
| Product concepts | Household ISP budget separate from per-device default quota; schedules initially TimeWindow rules; temporary pause implemented as an expiring claim |
| Safety | Keep real quotas dry-run and Docker forced rules dry-run until live counters/T8 checked. Proposed UI also refuses manual restriction on protected targets until an explicit unprotect step; current code only protects automatic actions |
| Implementation | Route-by-route flag with legacy access for parity; auth independent of flag; later serialized Agent ownership for current direct Web paths |
| Prototype scope | Every required screen family appears. It is a representative interaction model, not a complete implementation of every field/state. See coverage below; specifications retain all existing behavior |

## Prototype coverage / deliberate limits

| Required family | Visible / clickable | Limits |
|---|---|---|
| Onboarding + Safety Center | Six steps; sample test/discovery/category/default quota/notifications/done; evidence checklist + graduation guidance | No credential capture, real permission request, persistence/evidence or enforcement switch |
| Home | Health, budget, consumers, online count, attention, manual restrictions/previews, pause/bonus/block/guest flows | Health/budget/temporary claims are fictional proposals; Agent tick is explicitly simulated |
| Devices + detail | Six realistic devices, search/category/online filters, category sorting, bulk select; all six details; TV usage/quota/claims/rules/flags/history + identity sheet | Person grouping is disabled pending backend; six-row desktop grouping uses category sorting; only TV has a detailed chart and two-claim enforcing scenario. Identity editor is a TV example |
| Usage | Day/week/month; solid Internet/hatched LAN; totals/category/device rankings; chart table; selected-period CSV; budget preview | Forecast is clearly unavailable for real data until verified. Household period CSV uses proportional fake device totals; no hourly view or real ISP query |
| Automations | Hub, quota sheet (cap/range/action/warnings/dry-run/exempt), inheritance preview, six template entries; five-step rule wizard with all trigger/action choices; schedules/policies | Focused demo quota state updates Television; default/device sheets demonstrate the editor pattern rather than full independent policy persistence. Wizard previews one action and sample target; spec retains multi-action/device selectors, full validation and all period/repeat semantics |
| Activity/inbox | Type-filtered timeline, Why links, sample inbox/bonus/restore/read feedback | No persistent unread state, event pagination, real tokens or fully persisted read state |
| Settings | Router, guest/IoT/main read-only, notifications/quiet hours/event toggles/subscriptions, Safety, security proposal, LAN, HTTPS/install, health, backup, About | Future controls open labelled explanations. No auth/backup/restore/install/push APIs; per-event/quiet edits are demonstration values |
| Cross-cutting | Adaptive nav, theme/seed/RTL, native sheets/dialogs, confirmations/Undo example, pending→simulated confirmation, search/keyboard, global scenario selector | Scenarios replace screen bodies consistently; real per-panel async/circuit, keyboard/screen-reader behavior and layout still need browser QA. Prototype is English; RTL is layout-only |

Do not copy prototype JavaScript into production. Translate the specifications into routable Razor pages, scoped read/command facades and small components. No raw-store mutation should be inferred from a demo button.

## Source and behavior audit

Reviewed requested sources: [CLAUDE.md](../../CLAUDE.md), [README](../../README.md), [Phase 5](../phase5-quotas-notifications-rules-plan.md), [rules](../rules.md), [notifications](../server-notifications.md), [HTTPS](../https-access.md), [LAN exclusion](../lan-traffic-exclusion.md), [wireless plan](../phase3-wireless-management-plan.md), [HA readiness](../phase4-home-assistant-readiness.md), [Home.razor](../../src/NetPilot.Web/Components/Pages/Home.razor), current Tabs/Layout components, app/page CSS and manifest. Source code is evidence only, untouched.

Concrete reconciliations:

- Rules T7a/b/c are implemented in code and current Phase 5/rules docs, despite CLAUDE.md's stale “planned” line; live UI/router checks remain open.
- Agent applies automatic quota/rule effects on the next tick (default 180 seconds). Current **force reapply/Sync all also attempt immediate Web writes**, and **manual refresh reconciles policies**, in addition to block/unblock/wireless/reboot/test direct paths. Specs preserve and disclose these until gateway migration.
- Quota/rule usage is intended as Internet-only after optional exclusions; without exporter (or on failure), router LAN bytes can remain included. Do not imply perfect Internet attribution. Historical totals are not reclassified.
- GB formatter mismatch and required health/receipt/backend additions are explicit gaps. Main wireless remains read-only; IoT password and radio writes unsupported; reboot and coupling remain unverified; model/firmware reads already use the confirmed R6 endpoint.
- Current PWA worker does not cache pages. Offline shell, durable inbox, billing cycle, profiles, auth, generalized bonus/pause, restore and historical replay are proposed work.

## Validation

Run locally:

```sh
node --check docs/design/prototype/app.js
node --check docs/design/prototype/verify.mjs
node docs/design/prototype/verify.mjs
```

Validation evidence is static unless explicitly stated:

- JS syntax check; runtime smoke uses a **stub DOM** across 34 routes × 6 scenarios (204 rendered states), plus quota sheets, five wizard steps and every trigger/action presentation. Checks device filtering, pending→demo confirmation, save, guest confirmation and search. It is not proof of browser event/layout semantics.
- Relative Markdown and HTML links/assets checked, including rendered screen links. No external asset dependencies. Hash routes are handled by the local script; production Blazor routes are specified separately.
- WCAG relative-luminance calculation on tokens: **minimum normal-text ratio 6.35:1 light; 7.16:1 dark** across accent/on-accent, container/on-container, six semantic container pairs, on-surface and secondary text against six surface tones. Outline against page surface **4.26:1 light / 5.83:1 dark**. Violet primary pairs also exceed 4.5:1. Values are **measured from CSS hex values**, not visual estimates. This is not a full rendered contrast/AA audit: compositing, hover/disabled layers, chart distinctions, focus on every background and user-selected colors need real-browser checks.
- Chrome is installed, but isolated headless launch exited **134** before producing DOM/screenshot in this sandbox. No Playwright/Puppeteer runner or cached bUnit/Playwright NuGet package was available. No real browser visual rendering at 390/768/1280, console capture, TalkBack/keyboard/RTL text or PWA OS flow was verified. Static CSS breakpoints and native-dialog behavior are a proposed implementation, not a rendering certification.
- No production `dotnet run`, router call, solution build/test, deployment or phone action. A docs-only package does not need a production test run. `git status --short` must show only `docs/design/`; no commit created.

Remaining review gate: visually inspect the local file at all three widths and themes; check contrast overlays, sheet keyboard/focus restoration and fictional-data consistency; then approve the design direction and plan implementation separately.

## M1 verification handoff

The solution builds against the cached MudBlazor 9.11.0 package. `NetPilot.Data.Tests/Web/ShellRenderingTests.cs` uses the framework's HtmlRenderer and a temporary LiteDB database to render AppShell with each of the six placeholder pages, including existing quota/rules/notification controls. It adds a Web project reference and an ASP.NET Core framework reference; no test packages are added. Static rendering does not execute JS, focus behavior or responsive CSS.

Before accepting the UI, check 390, 600, 601, 1199 and 1200 CSS-pixel widths (including 200% zoom): the five phone destinations, rail/drawer boundaries, parent selection on nested routes, visible keyboard focus, safe-area padding, persistent mode strip and usable embedded editors. Check Search and Ctrl/Cmd+K, page/device filtering, navigation closing the dialog, Escape/backdrop/close and focus return; verify fullscreen search on phones. Try System/Light/Dark on reload and after OS theme changes, blocked localStorage, and slow circuit startup on a dark system. Check that `/legacy` loads without Mud CSS and looks unchanged, including returning via Classic-view links and the redesign-disabled root. With shared-password login enabled, verify Sign out submits POST `/logout` with an antiforgery token.

Docker Web image build and real-browser visual/interaction checks remain with the orchestrator. The Web csproj carries the explicit package version; there is no Directory.Packages.props. Dockerfile.web already copies that csproj before `dotnet restore`, so the image restore includes MudBlazor without another Docker asset change. Keep rules and quota enforcement in dry-run.
