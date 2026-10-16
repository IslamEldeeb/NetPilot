# 01 · Design system

**Direction: a calm household instrument.** Teal Material You surfaces, rounded tonal containers, generous phone controls, typography-led hierarchy. Purple means observation/dry-run; green means enforcing; neither means “safe to turn on.” Persistent text labels explain both.

## Implementation choice

No new NuGet packages. **Choose native Blazor components + plain token CSS**, with a small JS module for focus, theme preference and browser capabilities. Do not add Material Web custom elements: their shadow DOM/form integration is unnecessary here.

| Option | Licence | Size / delivery | Accessibility | Blazor Server fit | Decision |
|---|---|---|---|---|---|
| Native Razor + CSS; Material Symbols optional | App code follows repository licence (README currently TBD); Symbols Apache 2.0 | Zero library runtime. Proposed budget ≤35 KB gzip CSS + ≤10 KB gzip JS excluding icons/fonts; targets, not measured library sizes | Native inputs/dialog/table give semantics; team owns focus, errors, screen-reader QA | Direct EditForm/EventCallback integration; no new package or runtime | **Pick**: exact M3 roles, offline delivery, small controlled surface |
| MudBlazor | MIT | NuGet assembly + shared CSS/JS, fonts/icons; larger dependency surface. Exact version/build transfer not benchmarked | Components provide behavior, still requires application keyboard and AT testing | Established Blazor installation/providers; useful for a large enterprise control set | Does not satisfy no-new-package constraint; styling migration costs exceed benefit |
| Radzen.Blazor | MIT component source (commercial tooling separate) | NuGet assembly + theme CSS/JS. Exact shipped/gzip sizes unmeasured, not interchangeable with package download size | Vendor accessibility guidance and conformance report; no blanket app guarantee | Native Blazor components, Material 3 theme available | Strong alternative if dependency policy changes; unnecessary breadth today |

Licence/installation verified 16 Oct 2026: [MudBlazor licence](https://github.com/MudBlazor/MudBlazor/blob/dev/LICENSE), [installation](https://mudblazor.com/getting-started/installation), [Radzen licence](https://github.com/radzenhq/radzen-blazor/blob/master/LICENSE), [Radzen accessibility](https://blazor.radzen.com/accessibility). Relative size judgments are architectural estimates, not measured comparisons. Repository licensing must be settled before distributing a branded UI kit.

## Ready-to-paste tokens

Use [tokens.css](tokens.css) unchanged as the starting stylesheet. Import before components. Role names follow [Material 3 color roles](https://m3.material.io/styles/color/roles); the complete 15-style [M3 type scale](https://m3.material.io/styles/typography/applying-type) is included as font shorthand variables.

| Roles | Intended use |
|---|---|
| primary / on-primary / container / on-container | Main CTA, selection, household usage hero |
| secondary + corresponding on/container roles | Quiet controls, supporting metadata |
| tertiary + corresponding on/container roles | Secondary data series and preview highlights |
| surface; container lowest/low/default/high/highest | Page, cards, editor group, selected surface, overlay; tonal layering first |
| on-surface / on-surface-variant | Body/headings / secondary text; never lower opacity for essential data |
| outline / outline-variant | Required control boundaries / decorative dividers |
| error + corresponding on/container roles | Invalid field and failed operation; not dry-run |
| inverse-surface / inverse-on-surface / inverse-primary | Snackbar and its action |
| `--np-ok`, warning, dry-run, enforcing, blocked, throttled + `-container` | Semantic text/icon + paired container; always pair with words and a distinct symbol |
| chart-internet / chart-lan | Solid Internet; hatched LAN; direct labels plus accessible table |

Theme: follow `prefers-color-scheme` when no override. Manual **System / Light / Dark** sets/removes `data-theme` on `<html>` and stores only this preference locally. Apply before first paint; storage failure falls back to System. Update browser theme-color. No circuit round-trip for theme.

Seed color: ship audited Teal and Violet presets (prototype selector). For arbitrary seed later, generate all light/dark role pairs offline using [Material Color Utilities](https://github.com/material-foundation/material-color-utilities), export CSS and rerun contrast checks. CSS hue rotation or setting primary alone is not Material dynamic color. No Android wallpaper access is assumed. Semantic colors stay stable across seeds.

## Geometry, density and motion

- 4px spacing grid; 16/24/32px responsive gutters. Cards 24px padding desktop, 20px phone; related fields 12px gaps, sections 24px.
- Shape: 4px tiny status, 8px fields, 12px small containers, 16px cards, 28px hero/sheets, full pill buttons. Editors max 480px wide desktop; phone full-width bottom sheet up to 92dvh.
- Elevation 0 for most cards; 1 for raised controls; 2 floating navigation; 3 dialogs. Surface tones communicate dark-theme hierarchy; shadows are secondary.
- Default rows ≥64px; device cards ≥104px. Optional comfortable/compact desktop density can reduce rows to 56px; **all interactive hit areas stay ≥48×48 CSS px**. No compact mode on touch pointers.
- 120ms state response, 240ms sheet, 360ms page entrance maximum; standard easing token. Animate opacity/transform only, no counters that imply live telemetry. Reduced motion disables animation/shimmer; no essential information depends on motion.
- State layer uses current content color: hover 8%, focus/press 12%; keep base text opaque. Disabled foreground 38%, container 12% only for genuinely unavailable actions with adjacent reason. Disabled inputs retain readable value summaries.

## Reusable component contract

| Component / inputs | Appearance + behavior | States / keyboard / announcements |
|---|---|---|
| `NpButton(variant, busy, disabledReason)` | Filled primary / tonal secondary / outlined / text / danger; ≥48px, 24px icon, 16px horizontal padding | Hover/press layer; busy spinner + verb “Saving”; Enter/Space; keep width and announce completion |
| `ModeStrip(quotaSummary, rulesEffectiveMode, configSource)` | Sticky calm container below app bar; **Quotas: Dry-run · Rules: Forced dry-run**; Mixed/Disabled/Unknown allowed | Link to Safety; no success color for unknown. Wrapping text on phone; never hidden by scroll |
| `StatusChip(kind, label, timestamp)` | Noninteractive pill, 24–32px visual height; actionable version 48px hit box | Text + icon; `role=status` only for transitions, not every repeated chip |
| `ActionReceipt(id, requestedVersion, state, timestamp, owners)` | Saved → Awaiting Agent → Applied/Partly applied/Failed; details link | No completion countdown; “Applied” only from durable correlated Agent confirmation (backend gap); direct Web paths use Command sent/Read back |
| `DeviceRow(model, selected)` / `DeviceCard` | Table ≥1200px, cards below; name link, category, medium, age, usage, restriction | Separate checkbox/action buttons; no clickable div; stable MAC keys; expand secondary fields instead of ellipsis for MAC |
| `UsageMeter(used, cap, mode, confidence)` | Linear/ring; text `4.2 of 5 GB · 84%`; clamp visual to 100%, retain actual text | `progressbar` name/min/max/value; no cap = plain amount, unknown = dash, never zero |
| `ClaimStack(claims, basePolicy)` | Winning Block else lowest symmetric Kbps; owner cards, saved intent vs observed state | Release specific owner; show remaining restrictions before confirm; deleted-source fallback uses stable source ID |
| `EditorSheet(title, draft, errors, dirty)` | Bottom on phone; side on larger screens; sticky title + footer; scroll body only | Modal dialog labelled by title; focus first field; trap focus, Escape invokes discard confirmation if dirty; return focus to opener |
| `NpField(label, hint, error)` | 56px field + persistent external label; error under field; optional hint | Associated for/id; `aria-invalid`, describedby hint/error; submit summary links focus invalid field; no color-only validation |
| `TimelineEvent(type, mode, cause, outcome)` | Time / icon / plain sentence / Why link; group local date | Ordered list, chronological headings; dry-run says “Would have”; avoids JSON or tokens |
| `EmptyState(title, body, action)` | Small inline SVG diagram, one main CTA, no illustration conveying unique facts | SVG aria-hidden; heading and action real text; no-data vs no-filter-results distinguished |
| `Snackbar(message, action)` | Inverse surface, max 560px; persistent on error; Undo 10s plus durable Activity recovery | Polite live region; don't steal focus; dismiss button; pause timeout on focus/hover |

## Accessibility / content

Target [WCAG 2.2 AA](https://www.w3.org/TR/WCAG22/): normal text ≥4.5:1, large ≥3:1, required boundaries/icons ≥3:1. 48px is our product target, stricter than AA's 24px target-size criterion. Visible 3px focus ring with 3px offset; test it on every surface. Focus not obscured by sticky bars, sheets or keyboards. A larger focus treatment is a design choice; Focus Appearance 2.4.13 itself is AAA.

Use Material Symbols Rounded at 24px, weight 400, optical size 24; selected filled variant. [Symbols licence/guide](https://developers.google.com/fonts/docs/material_symbols). Production self-host a subset or inline licensed SVG paths; never leave ligature words visible while font is unavailable. Prototype uses dependency-free inline SVG equivalents with accessible button labels; exact Symbols font is not bundled. System font fallback is intentional, with no network request.

Strings: sentence case; “Internet”, “LAN excluded”, “GB (decimal)”, “Kbps” and “Mbps” unambiguous. Never say “Connected” from saved configuration alone. Prefer “No fresh reading since 21:39.” Dates/times use Africa/Cairo including DST. MAC/IP in `<bdi dir="ltr">`; app layouts use logical margins, paddings and inset properties. See [screen patterns](03-screens.md#shared-interaction-and-content-contracts) for i18n and focus behavior.

Contrast measurements and remaining QA are recorded in [README](README.md#validation). This is a design target, not a full WCAG conformance claim.
