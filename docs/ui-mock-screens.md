# Mock Screens — NetPilot Dashboard

**Status:** the mock screens and the design prototype are one design. `mockups/*.html` are static
snapshots generated from `docs/design/prototype/` and share its `tokens.css` + `prototype.css`, so
they cannot drift apart visually. Regenerate with `python3 tools/generate_mockups.py`.

**Source of truth:** `docs/design/prototype/index.html` (interactive) and `docs/design/03-screens.md`
(behaviour). The production app in `src/NetPilot.Web/` implements the same screens.

**Legend** (from the prototype)

- `[B]` button · `[T]` text field · `[C]` chip · `[P]` pill · `[I]` icon
- `▮` filled element · `░` muted element · `━` divider
- Chips: `🔵` primary · `🟢` online/ok · `🟡` warning/throttled · `🔴` blocked/error · `🟣` dry-run · `⚪` muted

**Color system** (`docs/design/tokens.css`)

| Token | Light | Usage |
|---|---|---|
| `--md-sys-color-primary` | `#006a60` | links, primary buttons, eyebrows |
| `--np-ok` | `#155d36` | online chip, healthy state |
| `--np-warning` | `#704600` | throttled, stale observation |
| `--md-sys-color-error` | `#ba1a1a` | blocked, failed write |
| `--np-dry-run` | `#514086` | dry-run chip and banner |
| `--np-enforcing` | `#00584b` | enforcing chip |
| `--np-throttled` | `#713f00` | throttle chip |

---

## 1. HOME — `/` · `mockups/home.html`

```
┌─────────────────────────────────────────────────────────────┐
│  Friday, 16 October                        [⏸ Pause internet] │
│  A little clarity for your network.                          │
│  Your household at a glance. All times in Cairo.             │
├─────────────────────────────────────────────────────────────┤
│  🟢 Archer AX53  [Last read 21:39]        → Network health   │
│     5 devices online · Agent completed a tick 3 min ago      │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ Internet today            │  │ Needs your attention   │  │
│  │        8.4 GB             │  │ ⚠ TV is at 84% of quota │  │
│  │ of a 15 GB daily budget   │  │ ⚠ New device: IP camera │  │
│  │ 6.6 GB left to enjoy      │  │ 🍃 Verify your LAN source│ │
│  │      (56% ring)           │  │                        │  │
│  │ Month · 214 of 300 GB     │  │                        │  │
│  └───────────────────────────┘  └────────────────────────┘  │
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ Top Internet consumers    │  │ Restrictions & previews │  │
│  │ 📺 Samsung TV   4.2 GB    │  │ Samsung TV  512 Kbps    │  │
│  │ 💻 Laptop       2.1 GB    │  │ PS5        Manual block │  │
│  └───────────────────────────┘  └────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Make a quick adjustment                                │  │
│  │ [⏸ Pause internet] [+ Add bonus data] [⛔ Block] [WiFi] │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

- Household card first: router name, last-read chip, online count, next tick.
- Usage hero: big number, daily budget line, 56% ring, month row, “Explore usage”.
- “Needs your attention” only lists actionable items.
- “Make a quick adjustment” is the quick-action grid, four tiles.

---

## 2. DEVICES — `/devices` · `mockups/devices.html`

```
┌─────────────────────────────────────────────────────────────┐
│  Your network                              [⟳ Refresh router]│
│  Devices                                                       │
│  6 known devices · 5 online now                               │
├─────────────────────────────────────────────────────────────┤
│  [Find a device…] [Category ▾] [Connection ▾] [Group by ▾]   │
├─────────────────────────────────────────────────────────────┤
│  ☑ │ Device / category        │ Connection │ Internet │ Restriction
│  ☐ │ 📱 Pixel phone            │ 🟢 Online │ 1.4 GB   │ 🟢 Protected
│    │    Mobile · 192.168.1.31   │ WiFi·5GHz │ LAN 0.1GB│
│  ☐ │ 📺 Samsung TV             │ 🟢 Online │ 4.2 GB   │ 🟡 512 Kbps
│    │    Television · …1.50      │ WiFi·5GHz │ LAN 12GB │   · manual
│  ☐ │ 🖥 Jellyfin server        │ 🟢 Online │ 0 GB     │ 🟢 Protected
│  ☐ │ 🎮 PS5                    │ ⚪ Offline│ 0.6 GB   │ 🔴 Blocked
│  ☐ │ 📷 IP camera              │ 🟢 Online │ 0.1 GB   │ ⚪ No restriction
│  ☐ │ 💻 Laptop                 │ 🟢 Online │ 2.1 GB   │ ⚪ No restriction
└─────────────────────────────────────────────────────────────┘
```

- Filters: search, category, connection (all / online / offline), group by (category / none / person-disabled).
- Table shows identity tile, category + IP, connection chip, today’s Internet, restriction chip.
- Mobile/tablet use the card layout (`.device-cards`).
- Selection bar appears with `N selected`, “Pause selected”, “Clear”.

---

## 3. DEVICE DETAIL — `/devices/{mac}` · `mockups/device-detail.html`

```
┌─────────────────────────────────────────────────────────────┐
│  ← Devices / Samsung TV                                     │
│  📺  Television                                             │
│      Samsung TV                                    [⏸ Pause]│
│      02-00-00-00-00-02 · 192.168.1.50          [⚙ Edit identity]│
│  [🟢 Online · read 21:39] [WiFi · 5 GHz] [🟡 512 Kbps · manual]│
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ Internet usage            │  │ Daily quota            │  │
│  │ 4.2 GB │ 89.2 GB │ 12 GB  │  │   (84% ring)           │  │
│  │  chart + data table       │  │ 4.2 of 5 GB            │  │
│  └───────────────────────────┘  │ policy trail           │  │
│                                 │ [⚙ Edit quota]         │  │
│  ┌───────────────────────────┐  └────────────────────────┘  │
│  │ Why is this device        │  ┌────────────────────────┐  │
│  │ restricted?               │  │ Device settings        │  │
│  │ detail claims + release   │  │ ☐ Protected device     │  │
│  └───────────────────────────┘  │ ☐ LAN-only device      │  │
│                                 │ [⛔ Block device]        │  │
│  ┌───────────────────────────┐  └────────────────────────┘  │
│  │ Rules & bedtime           │  ┌────────────────────────┐  │
│  │ 🕘 Bedtime throttle       │  │ Recent history         │  │
│  └───────────────────────────┘  └────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

- Identity header: large tile, category eyebrow, name, MAC · IP, actions.
- Pills row: state with read time, connection medium, restriction.
- Daily quota shows the ring, the matched-policy trail and the dry-run state.

---

## 4. USAGE — `/usage` · `mockups/usage.html`

```
┌─────────────────────────────────────────────────────────────┐
│  Understand your data                       [⬇ Export CSV]   │
│  Usage                                                         │
│  Internet usage, with local traffic separated.                │
├─────────────────────────────────────────────────────────────┤
│  [Day] [Week] [Month]        10–16 October 2026 · Africa/Cairo│
│  🌿 LAN source responds, but counters are not live-verified.  │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ Internet & local traffic  │  │ A look ahead           │  │
│  │ 111.6 GB │ 122 GB │ 5/6  │  │ Around 21 Oct          │  │
│  │  bar-pair chart           │  │ [⚙ Edit ISP budget]    │  │
│  └───────────────────────────┘  └────────────────────────┘  │
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ Top talkers today         │  │ By category · today     │  │
│  └───────────────────────────┘  └────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

- Chart pairs Internet (solid) with LAN excluded (hatched) per period, plus a data table.
- The production page swaps the sample forecast for the real household ISP budget and cycle estimate.

---

## 5. AUTOMATIONS — `/automations` · `mockups/automations-quotas.html`

```
┌─────────────────────────────────────────────────────────────┐
│  Keep it fair                                                │
│  Automations                                                 │
│  Simple defaults. Clear causes. You stay in control.         │
│  [Overview] [Quotas] [Rules] [Schedules] [Speed policies]    │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ 📊 Quotas                 │  │ ⚡ Rules               │  │
│  │ Daily & monthly limits    │  │ When this, do that     │  │
│  │ [→ Edit quotas]           │  │ [→ Explore rules]      │  │
│  └───────────────────────────┘  └────────────────────────┘  │
│  ┌───────────────────────────┐  ┌────────────────────────┐  │
│  │ 🕘 Schedules              │  │ 🎛 Speed policies       │  │
│  │ A quieter bedtime         │  │ A fair share of …      │  │
│  │ [→ Set bedtime]           │  │ [→ Manage policies]    │  │
│  └───────────────────────────┘  └────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

Sub-sections (production app):

- **Quotas** — banner, “Global default” + “Category overrides” cards, “Effective policy preview”
  (policy trail), “Safety controls” (protected / LAN-only counts), today’s progress, device flags.
- **Rules** — forced-dry-run banner, “Saved rules” list (chips: enabled, forced dry-run), template grid.
- **Schedules** — “Bedtime, with a safe morning” + “Keep management reachable”.
- **Speed policies** — “Category speed policies” card, bulk edit, per-category rows, “Save all” / “Sync all”.

---

## 6. ACTIVITY — `/activity` · `mockups/activity.html`

```
┌─────────────────────────────────────────────────────────────┐
│  Know what happened                                          │
│  Activity                                                    │
│  A clear timeline of your network. Times in Africa/Cairo.     │
│  [Timeline] [Inbox · 2]                                      │
├─────────────────────────────────────────────────────────────┤
│  [Show events ▾]            Friday, 16 October 2026          │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ 21:39 🖥  Agent tick completed          [System]      │  │
│  │ 21:36 📊  Samsung TV reached 80% of its quota [Dry-run]│  │
│  │ 21:12 ⚡  Binge alert evaluated            [Rule]      │  │
│  │ 20:51 📱  Pixel phone came online          [Device]   │  │
│  │ 19:12 🔔  Notification accepted by push  [Transport]  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

- Timeline rows: time, symbol tile, title + semantic chip, message, source link.
- Inbox variant lists notification events with “Mark read”; delivery attempts sit in their own card.

---

## 7. SETTINGS — `/settings` · `mockups/settings.html`

```
┌─────────────────────────────────────────────────────────────┐
│  Your preferences                                            │
│  Settings                                                    │
│  A dependable home network starts with clear controls.        │
├──────────────┬──────────────────────────────────────────────┤
│ Router       │  Your household controls                     │
│ Wireless     │  ⚙ Router          →                          │
│ Notifications│  📶 Wireless        →                          │
│ Safety Center│  🔔 Notifications   →                          │
│ Security     │  🛡 Safety Center   →                          │
│ LAN source   │  🔒 Security        →                          │
│ HTTPS/Install│  🍃 LAN source      →                          │
│ System health│  📱 HTTPS / Install →                          │
│ Backup       │  📊 System health   →                          │
│ About        │  💾 Backup          →                          │
└──────────────┴──────────────────────────────────────────────┘
```

---

## 8. COMPONENT INVENTORY

Prototype classes are the contract (`docs/design/prototype/prototype.css`); the Web app ports them in
`src/NetPilot.Web/wwwroot/css/app-shell.css`:

| Prototype class | Used for |
|---|---|
| `page-head`, `eyebrow`, `actions` | screen headers |
| `card`, `toned`, `grid`, `span-4/6/8/12`, `tablet-full` | layout |
| `section-head`, `list-row`, `grow` | card headers and rows |
| `chip` + `ok/dry/warning/blocked/throttled/live` | semantic state chips |
| `button`, `tonal`, `outline`, `text-button`, `danger` | actions |
| `field`, `toolbar`, `search-field`, `segmented`, `check-row` | inputs and filters |
| `household`, `symbol-tile`, `hero`, `ring`, `meter`, `consumer`, `notice`, `quick-grid` | Home parts |
| `device-table`, `device-identity`, `select-box`, `selection-bar`, `device-card` | Devices |
| `identity-header`, `breadcrumbs`, `pills`, `policy-trail`, `detail-claim`, `stat-grid` | Device detail |
| `usage-chart`, `bar-pair`, `bar`, `legend`, `swatch` | charts |
| `section-tabs`, `automation-card`, `template-grid`, `template` | Automations |
| `timeline` | Activity |
| `settings-layout`, `settings-nav`, `key-values`, `form-grid`, `form-stack` | Settings |
| `empty-state`, `skeleton`, `banner`, `receipt` | states |
