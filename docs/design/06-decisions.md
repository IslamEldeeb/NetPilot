# 06 · Owner decisions and amended roadmap

Decided 16 Oct 2026. These override anything in 01–05 that conflicts.

## Decisions

| # | Decision |
|---|---|
| D1 | Native Razor + CSS only. Tokens from `tokens.css` live in `wwwroot/css/tokens.css`. Icons are an inline-SVG `Icon` component (no icon font, no CDN, works offline). No new NuGet packages. |
| D2 | Adaptive navigation exactly as `02-information-architecture.md`: bottom bar ≤600, rail 601–1199, drawer ≥1200; five destinations + Settings in the app bar; persistent mode strip. |
| D3 | Feature flag `NetPilot:Ui:RedesignEnabled` (env `NETPILOT_UI_REDESIGN`, default **true** once the shell lands). Today's Home stays reachable at `/legacy` until every screen has parity, then it is deleted. |
| D4 | **Login is one shared password.** `NetPilot:Web:Password` (env `NETPILOT_WEB_PASSWORD`), empty = login off, cookie session, no users, no database. This replaces T11a/T11b (admin credential store, sessions, recovery). |
| D5 | Router session ownership (T13) is **deferred**. The Web keeps its current direct actions (test, refresh, block, wireless, reboot). It stays documented as a known risk, not a blocker. |
| D6 | "Saved vs applied" is simplified: the Agent writes a heartbeat; the UI shows Saved / Pending (saved after the last completed tick) / Applied (a tick completed after the save). Per-target acknowledgements (T12b) are deferred. |
| D7 | Data sizes are **decimal GB** (1 GB = 1,000,000,000 bytes) everywhere, through one shared formatter. |
| D8 | Backup is a Web "Download backup" zip of the LiteDB file plus the Data Protection key ring (both are needed to restore). Restore is documented manual steps first, UI restore later. |
| D9 | Real enforcement stays dry-run. The UI never offers a one-click switch from dry-run to enforcing; it shows the checklist and the config variable to change. |

## Amended execution order

Work is delegated in small batches; each batch is reviewed, gated and committed before the next.

| Batch | Tasks | Notes |
|---|---|---|
| A1 | T9a tokens + theme, T9b primitives, T10a shell/nav/flag/`/legacy`, shared decimal formatter | UI only |
| A2 | Simple login (D4) + Agent heartbeat and apply-status logic (D6) | Backend; runs in parallel with A1 in a separate worktree |
| B | T10b route extraction, T14a Devices, T14c Device detail (+ identity overrides T14b) | |
| C | T16a Home, T15a Usage (+ CSV), T15b ISP budget/billing day, T16b/c pause + bonus | |
| D | T17 Automations, T18 Activity/inbox, T19 Settings, T20a backup, T21 onboarding + Safety Center | |
| E | T22 people/schedules, T23 RTL/a11y/PWA polish, T24 integrations (only if time) | |
