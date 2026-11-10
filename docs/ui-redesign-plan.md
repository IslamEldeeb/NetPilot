# NetPilot UI — Design Match Plan

**Goal:** Match the current implemented MudBlazor UI to the design prototype at `docs/design/prototype/index.html` — layout, colors, typography, components, navigation, and all UX patterns from `01-design-system.md`, `02-information-architecture.md`, and `03-screens.md`.

**Theme status:** Colors already match exactly (teal primary `#006a60`, surface `#f6faf7`, np-ok/dry-run/enforcing/blocked/throttled pairs). No theme color changes needed.

**Scope:** Foundation (layout shell + CSS) → Page-by-page visual match → Component library alignment → UX interaction patterns.

**Status (updated):** every screen now renders with the prototype's class system and copy. See
"Delivered" at the bottom for what landed and what is still open.

---

## 1. Foundation — Layout Shell & CSS (`app-shell.css` + `AppShell.razor`)

Current: Has drawer/rail/bottom bar, app bar, mode strip, correct breakpoints.  
Target: Match prototype geometry and component styles exactly.

| Element | Prototype spec | Current state | Action |
|---|---|---|---|
| Navigation drawer | 248px fixed left, brand + nav items + utility + network label | 248px drawer exists | Verify brand mark + network label styling |
| Rail | 88px mini, 601–1199px | Exists | Verify styling |
| Bottom bar | 80px fixed, ≤600px, 5 labelled icon buttons | Exists | Verify active state = tonal pill |
| App bar | 72px, context + search + theme + settings | Exists (64px) | Adjust height + context styling |
| Mode strip | Sticky below app bar, dry-run/enforcing modes, Safety link | Exists | Verify exact colors + copy |
| Main content | 36px top padding, max 1424px, cards 24px pad / 20px radius | Exists | Adjust padding + radius |
| Cards | `surface-container-lowest`, 1px outline-variant border, 28px radius | 28px radius exists | Verify border + padding |
| Focus ring | 3px primary, 3px offset | 2px exists | Update to 3px |
| Chips | ok/dry/warning/blocked/throttled/live semantic pairs | Partial | Add all semantic chip styles |
| Buttons | Filled/tonal/outlined/text/danger, ≥48px, pill-shaped (999px) | Partial | Standardize |
| Fields | 52px height, 12px radius, outline inputs | Partial | Standardize |

**Files:** `wwwroot/css/app-shell.css`, `Components/Layout/AppShell.razor`, `Components/Layout/AdaptiveNav.razor`

---

## 2. Component Library Alignment

Create/update design-system wrappers to match prototype component patterns:

| Component | Prototype pattern | Current | Action |
|---|---|---|---|
| Chip | `chip { gap:5px, padding:5px 10px, radius 999px, font 11px/18px 650 }` | StatusPill exists | Add semantic chip variants (ok/dry/warning/blocked/throttled/live) |
| Household card | flex row, symbol tile + summary, surface-container, 20px radius | — | Create `HouseholdCard.razor` |
| Hero card | big number + ring chart + meter, primary-container bg | BudgetCard exists | Align visual |
| Consumer card | symbol tile + name + meter + number | — | Create `ConsumerCard.razor` |
| Notice | flex row, icon left, text + link right, border-bottom | — | Create `Notice.razor` |
| Stat grid | 3 columns, metric + label | — | Create `StatGrid.razor` |
| Key-values | dt/dd grid, muted labels | — | Create `KeyValues.razor` |
| Timeline | 64px time + 36px symbol + content, bordered | Activity exists | Align visual |
| Receipt | pending/applied banner, clock/check icons | — | Create `ActionReceipt.razor` |
| Empty state | symbol tile 80px, centered, one CTA | EmptyState exists | Align visual |

**Files:** `Components/DesignSystem/` new components + update existing

---

## 3. Page-by-Page Visual Match

### Home `/`
Prototype: Household health card → Usage hero (big number 8.4GB / 15GB budget + ring 56% + meter) → Attention notices → Restrictions → Top consumers (consumer cards) → Quick actions (4-grid)
Current: Has similar content but different visual layout
**Action:** Restructure HomeDashboard.razor to match prototype layout

### Devices `/devices`
Prototype: Toolbar with search/category/online/group filters → table (desktop) or cards (mobile/tablet) → device identity (symbol + name + category + IP) → connection chip → usage → restriction chip
Current: Table + cards exist, filters exist
**Action:** Align row/card visual to prototype (device-identity div, restriction chip, connection chip with medium)

### Device detail `/devices/{mac}`
Prototype: Identity header (symbol tile large + name + category eyebrow + MAC/IP) → pills (online/medium/restriction) → grid: Internet usage (stat grid) + Daily quota (ring) + Why restricted (detail claims) + Device settings (check rows + block button) + Rules & bedtime + Recent history
Current: Has most content but different layout (cards stacked, not grid)
**Action:** Restructure DevicePage.razor to match prototype grid + identity header + pills

### Usage `/usage`
Prototype: Segmented period control → stat grid (Internet/LAN/devices) → usage chart (bar pairs Internet+LAN hatched) → top talkers (consumer cards) → by category
Current: Has similar but different visual (table + chart)
**Action:** Align to prototype (bar-pair chart, stat grid, consumer cards)

### Automations `/automations/*`
Prototype: Hub cards (Quotas/Rules/Schedules/Speed policies) → section tabs → quota sheet + rules list + templates + speed policies
Current: Has all subpages
**Action:** Align card/list visuals to prototype

### Activity `/activity`
Prototype: Section tabs (Timeline/Inbox) → filter toolbar → timeline (time + symbol tile + row between title + chip)
Current: Timeline exists
**Action:** Align timeline row visual to prototype

### Settings `/settings/*`
Prototype: Settings layout (left nav list + content) → form-stack / form-grid → key-values → check-rows
Current: Has sections
**Action:** Align to settings-layout pattern

---

## 4. UX Interaction Patterns

From `03-screens.md` shared contracts:

| Pattern | Spec | Current | Action |
|---|---|---|---|
| Mode strip | Sticky, "Quotas: Dry-run · Rules: Forced dry-run", Safety link | Exists | Verify exact copy + behavior |
| Receipts | "Takes effect on the Agent's next tick (about 3 min)" — no fake Applied | Partial | Standardize receipt text |
| Confirm | Cancel focused for dangerous actions, 10s Undo | Confirm exists | Verify cancel-focused |
| Search | Ctrl/Cmd+K, Escape closes, focus restore | Exists | Verify |
| Empty states | Illustration + title + one CTA | Exists | Verify |
| Loading | Skeleton matching card geometry, aria-busy | Partial | Add skeletons |
| Offline/Router offline | Cached read-only, stale markers, direct actions disabled | Partial | Verify |
| Dry-run | Purple chip, "Would throttle" wording, can still notify | Partial | Standardize dry-run chips |

---

## 5. Implementation Order (recommended)

1. **Phase 1 — Foundation:** `app-shell.css` layout/component updates + `AppShell.razor` structure + `AdaptiveNav.razor` brand/network label
2. **Phase 2 — Components:** New design-system components (HouseholdCard, ConsumerCard, Notice, StatGrid, KeyValues, ActionReceipt) + chip variants
3. **Phase 3 — Home page:** Match hero + household + attention + restrictions + consumers + quick actions
4. **Phase 4 — Devices + Device detail:** Table/card rows, identity header, pills, grid layout
5. **Phase 5 — Usage:** Segmented control, stat grid, bar-pair chart, consumer cards
6. **Phase 6 — Automations + Activity + Settings:** Match list/timeline/settings-layout visuals
7. **Phase 7 — UX patterns:** Receipts, undo, skeletons, empty states, offline markers

**Files NOT to touch:** `NetPilot.Core`, `NetPilot.Data`, `TpLink.Sdk`, `NetPilot.Providers.TpLink`, `NetPilot.Agent`, test projects — backend stays unchanged.

---

## 6. Verification

- `dotnet build NetPilot.slnx -c Release` — 0 errors
- `dotnet test NetPilot.slnx -c Release` — 841 tests pass
- Open `docs/design/prototype/index.html` and compare each screen visually
- Check 390px / 600px / 768px / 1200px breakpoints
- Check light/dark themes

---

## 7. Delivered

### Token + CSS foundation (`wwwroot/css/app-shell.css`)

- Material 3 role tokens (surface containers, outline, primary/secondary/tertiary containers)
  are now defined with the prototype's exact values. MudBlazor 9.11 ships no container
  roles, so the earlier port referenced undefined variables and silently lost borders,
  backgrounds and chip colors.
- Ported the missing prototype classes: `icon`, `muted`, `hint`, `code`, `eyebrow`,
  `breadcrumbs`, `identity-header`, `symbol-tile.large`, `table-scroll`, `selection-bar`,
  `select-box`, `device-cards`/`device-card`, `device-identity`, `screen-reader-only`,
  `legend`/`swatch`, `skeleton-wrap`, `segmented`, `metric`, `preview-label`,
  `pending-note`, `choice`, `setup-*`, `step-track`, `settings-label`, `app-context`,
  `app-tools`, `icon-button`, `mode-strip`.
- Breakpoints now match the prototype: the device table shows at ≥1200px, the card layout at
  601–1199px (two columns) and ≤600px (one column); `.tablet-full` spans the full grid.
- Bootstrap is loaded only for the classic `/legacy` view. Its `.row`/`.card` rules were
  overriding the prototype classes on every redesign page (children of `.row` were forced
  to `width:100%`).
- `FocusOnNavigate` still moves focus to the page heading for screen readers, but the ring
  is no longer painted around the title (the prototype shows none).

### Pages ported to the prototype markup

| Screen | File | Notes |
|---|---|---|
| Home | `Pages/HomeDashboard.razor` | household card, hero + ring, attention notices, consumers, restrictions, ISP budget, "Make a quick adjustment" |
| Devices | `Pages/Devices.razor` | prototype toolbar, one device table with 5 columns, card layout, selection bar |
| Device detail | `Pages/DevicePage.razor` | breadcrumbs, identity header, pills, 12-column grid (usage, quota ring, policy trail, detail claims, device settings, rules, history) |
| Usage | `Pages/Usage.razor` | period control, stat grid, bar-pair chart, budget card, talkers, by category, CSV export |
| Automations | `Pages/Automations.razor` | section tabs + overview hub (quotas, rules, schedules, speed policies) |
| Quotas | `Automations/QuotaPanel.razor` | global default, category overrides, effective-policy preview, safety controls, today's progress, device flags |
| Rules | `Automations/RulesPanel.razor` | forced-dry-run banner, saved rules list, template grid, history, JSON import/export |
| Speed policies | `Automations/SpeedPolicies.razor` | category rows, bulk edit, "Save all" / "Sync all" |
| Activity | `Pages/Activity.razor` | timeline (`<ol class="timeline">`), inbox variant, delivery attempts card |
| Settings | `Pages/Settings.razor` | left nav + content column |
| Safety Center | `Pages/Safety.razor` | effective modes, protected devices, evidence checklist, per-policy review |
| Setup | `Pages/Setup.razor` | step track, setup hero, card with step body and Back/Skip/Continue |

### Data honesty fixes

- Home and Usage show the real household ISP budget and cycle forecast instead of invented
  sample numbers; when no budget is saved the page says so.
- Device counts, ticks, usage and restrictions come from the stores; no placeholder values.

### Mock screens

`mockups/*.html` are generated from the prototype (`tools/generate_mockups.py`) and share its
`tokens.css` + `prototype.css`, so the mock screens and the prototype are the same design.
`docs/ui-mock-screens.md` now documents that contract instead of a second, diverging spec.

### Still open

- Devices page keeps bulk block/unblock buttons next to the prototype's "Pause selected"
  (feature parity over strict prototype copy).
- Settings sub-sections (router, wireless, notifications, backup, appearance) still use
  MudBlazor form controls inside the new settings frame.
- The classic `/legacy` view keeps its Bootstrap look until every screen has parity (D3).

