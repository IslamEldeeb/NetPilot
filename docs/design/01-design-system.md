# 01 · Design system

**Direction: a calm household instrument.** Teal Material You surfaces, rounded tonal containers, generous phone controls, typography-led hierarchy. Purple means observation/dry-run; green means enforcing; neither means “safe to turn on.” Persistent text labels explain both.

## Implementation choice

**Use MudBlazor 9.11.0 (MIT)** for the Web shell and primitives. M1 amends D1 to favor ready-made components and reduce CSS and interaction maintenance. This is the only new package. Its CSS/JS come from local `_content/MudBlazor/` static assets; all typography uses `system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif`. No font or CDN requests.

| Components option | Licence | Delivery / integration | Decision |
|---|---|---|---|
| MudBlazor 9.11.0 | MIT | NuGet assembly and local CSS/JS; theme, dialog, snackbar and popover providers in `AppShell` | **Chosen by M1** to reduce implementation and maintenance effort; app keyboard, touch and visual QA still required |
| Hand-rolled Razor primitives + token CSS | Repository app code | Replaced by Mud components; domain wrappers remain | Removed from production; the static design prototype is historical reference |
| Radzen.Blazor | MIT component source | Alternative library | Not adopted; no second component library |

## Theme mapping

[`NetPilotTheme`](../../src/NetPilot.Web/Components/DesignSystem/NetPilotTheme.cs) replaces `wwwroot/css/tokens.css`, which is deleted. [Design tokens](tokens.css) remain a reference for the prototype, not a production import.

| Reference roles | Mud theme mapping (light / dark) |
|---|---|
| primary / on-primary | Primary `#006a60` / `#80d5c5`; contrast `#ffffff` / `#00382f` |
| secondary / tertiary | Secondary `#4a635e` / `#b1ccc5`; Tertiary `#66558f` / `#d0bcff` |
| surface / surface-container-low | Background `#f6faf7` / `#0e1512`; Surface, drawer and app bar use white/low tonal layers |
| surface-container | BackgroundGray `#eaf0ec` / `#1b211e` |
| on-surface / on-surface-variant | TextPrimary `#171d1b` / `#dee4e0`; TextSecondary `#3f4945` / `#bec9c2` |
| outline / outline-variant | LinesInputs `#707974` / `#89938c`; LinesDefault and Divider `#bec9c2` / `#3f4945` |
| error / warning / ok / info | Error `#ba1a1a` / `#ffb4ab`; Warning `#704600` / `#ffdfa0`; Success `#155d36` / `#a6ebbc`; Info follows Secondary |
| dry-run / enforcing | Small `--np-dry-run`, `--np-enforcing` text/container pairs remain in `app-shell.css` because Mud has no domain-specific palette slots |
| secondary-container | `--np-selected` / `--np-on-selected` retain the tonal navigation selection |
| shape | Theme default control radius 12px; scoped paper/dialog radius 28px; minimum interactive targets 48px |

Mud owns its remaining component states, progress styling, elevation and typography. Unused prototype roles are not duplicated in production CSS.

The early `theme.js` reads `netpilot-theme` from localStorage in a try/catch, resolves System/Light/Dark, sets the resolved `data-theme`, background, color scheme and browser theme-color before body paint. Storage failure falls back to System. On a dark first visit the prerendered light shell is hidden until the circuit synchronizes `MudThemeProvider`; the background is already dark. The provider uses `GetSystemDarkModeAsync` and `WatchSystemDarkModeAsync` with a `SystemDarkModeChanged` callback; observation is enabled only while the preference is System. Returning to System reads the current OS preference again. Manual choices persist through the same JS API. The Teal palette ships; the prototype's Violet selector is not a production setting.

## Components

| Old primitive | Mud replacement |
|---|---|
| Icon | `MudIcon` with `Icons.Material.Outlined/Filled`; `MaterialIcon.For` maps navigation/category names, including phone, TV, laptop, camera, SportsEsports console, Storage server, speaker, printer, router and DevicesOther fallback |
| Button / Chip / StatusPill | `MudButton` / `MudChip`; one small `StatusPill` semantic wrapper |
| Card | `MudPaper` / `MudCard` |
| Meter / Ring | `MudProgressLinear` / `MudProgressCircular` at future call sites; unused custom implementations deleted |
| Field | `MudTextField`, `MudSelect`, `MudSwitch`, `MudCheckBox` directly; search uses `MudTextField` |
| Sheet | `AppSheet`, an inline `MudDialog`, fullscreen on xs (≤600px) |
| ConfirmDialog | Inject `Confirm`; `DangerAsync` uses `IDialogService.ShowMessageBoxAsync`, encoded body text and a danger action |
| SnackbarService / SnackbarHost | `ISnackbar` / `MudSnackbarProvider`; `AddUndo` extension supplies a 10-second Undo action |
| EmptyState / Skeleton | Small `EmptyState` over `MudStack`/`MudPaper`; `MudSkeleton` directly |
| ModeStrip / PendingChip | Domain wrappers over Mud components; modes retain existing store/configuration reads; PendingChip accepts Core `ApplyState` (Saved/Pending/Applied/AgentDown) from `ApplyStatus`/`AgentHealth` |

`ShellNavigation` remains the route ownership source. The shell uses a permanent (Mud `Persistent`) 248px drawer at ≥1200px, an 88px Mini drawer rail at 601–1199px, and a fixed `MudPaper` bottom bar with five labelled `MudIconButton` links at ≤600px. CSS handles exact viewport boundaries and safe-area insets. Search is an `AppSheet`/MudDialog, opened by the app-bar button or Ctrl/Cmd+K.

Classic-view links use a full document navigation (`Target="_top"`). `App.razor` omits Mud CSS on `/legacy` and whenever the redesign flag is off, preventing library resets/utilities from changing the existing Bootstrap/`np-*` UI. `LegacyHome` and `Components/Tabs/*` are unchanged. The few Bootstrap overrides for embedded tabs apply only under the new shell. No Agent health pill is added by M1. The app bar shows an antiforgery-protected POST Sign out form only when shared-password login is enabled.

## Geometry, density and motion

- 4px spacing grid; 16/24/32px responsive gutters. Cards 24px padding desktop, 20px phone; related fields 12px gaps, sections 24px.
- Shape: 12px controls, 28px cards/dialogs; Mud chips and selected navigation use pills. `AppSheet` is fullscreen on phones and a small dialog on larger viewports.
- Elevation 0 for most cards; 1 for raised controls; 2 floating navigation; 3 dialogs. Surface tones communicate dark-theme hierarchy; shadows are secondary.
- Default rows ≥64px; device cards ≥104px. Optional comfortable/compact desktop density can reduce rows to 56px; **all interactive hit areas stay ≥48×48 CSS px**. No compact mode on touch pointers.
- 120ms state response, 240ms sheet, 360ms page entrance maximum; standard easing token. Animate opacity/transform only, no counters that imply live telemetry. Reduced motion disables animation/shimmer; no essential information depends on motion.
- State layer uses current content color: hover 8%, focus/press 12%; keep base text opaque. Disabled foreground 38%, container 12% only for genuinely unavailable actions with adjacent reason. Disabled inputs retain readable value summaries.

## Screen interaction targets

These remain guidance for subsequent screen batches. The Components table above defines M1’s implemented primitives; domain patterns below may require later work.

| Component / inputs | Appearance + behavior | States / keyboard / announcements |
|---|---|---|
| `MudButton` + optional progress content | Filled primary / tonal secondary / outlined / text / danger; ≥48px, 24px icon, 16px horizontal padding | Hover/press layer; busy spinner + verb “Saving”; Enter/Space; keep width and announce completion |
| `ModeStrip(quotaSummary, rulesEffectiveMode, configSource)` | Sticky calm container below app bar; **Quotas: Dry-run · Rules: Forced dry-run**; Mixed/Disabled/Unknown allowed | Link to Safety; no success color for unknown. Wrapping text on phone; never hidden by scroll |
| `StatusPill` / `MudChip` | Noninteractive pill, 24–32px visual height; actionable version 48px hit box | Text + icon; `role=status` only for transitions, not every repeated chip |
| `ActionReceipt(id, requestedVersion, state, timestamp, owners)` | Saved → Awaiting Agent → Applied/Partly applied/Failed; details link | No completion countdown; “Applied” only from durable correlated Agent confirmation (backend gap); direct Web paths use Command sent/Read back |
| `DeviceRow(model, selected)` / `DeviceCard` | Table ≥1200px, cards below; name link, category, medium, age, usage, restriction | Separate checkbox/action buttons; no clickable div; stable MAC keys; expand secondary fields instead of ellipsis for MAC |
| `MudProgressLinear` / `MudProgressCircular` + usage text | Linear/ring; text `4.2 of 5 GB · 84%`; clamp visual to 100%, retain actual text | `progressbar` name/min/max/value; no cap = plain amount, unknown = dash, never zero |
| `ClaimStack(claims, basePolicy)` | Winning Block else lowest symmetric Kbps; owner cards, saved intent vs observed state | Release specific owner; show remaining restrictions before confirm; deleted-source fallback uses stable source ID |
| `AppSheet` + editor-owned draft/validation | Bottom on phone; side on larger screens; sticky title + footer; scroll body only | Modal dialog labelled by title; focus first field; trap focus, Escape invokes discard confirmation if dirty; return focus to opener |
| Mud input components with Label/HelperText/ErrorText | 56px field + persistent external label; error under field; optional hint | Associated for/id; `aria-invalid`, describedby hint/error; submit summary links focus invalid field; no color-only validation |
| `TimelineEvent(type, mode, cause, outcome)` | Time / icon / plain sentence / Why link; group local date | Ordered list, chronological headings; dry-run says “Would have”; avoids JSON or tokens |
| `EmptyState(title, body, action)` | Small inline SVG diagram, one main CTA, no illustration conveying unique facts | SVG aria-hidden; heading and action real text; no-data vs no-filter-results distinguished |
| `ISnackbar` + `AddUndo` | Inverse surface, max 560px; persistent on error; Undo 10s plus durable Activity recovery | Polite live region; don't steal focus; dismiss button; pause timeout on focus/hover |

## Accessibility / content

Target [WCAG 2.2 AA](https://www.w3.org/TR/WCAG22/): normal text ≥4.5:1, large ≥3:1, required boundaries/icons ≥3:1. 48px is our product target, stricter than AA's 24px target-size criterion. Visible 3px focus ring with 3px offset; test it on every surface. Focus not obscured by sticky bars, sheets or keyboards. A larger focus treatment is a design choice; Focus Appearance 2.4.13 itself is AAA.

Production icons use MudBlazor Material icon constants and `MudIcon`; no Material Symbols font or custom SVG component is needed. The static prototype keeps its historical inline SVG illustrations. Controls have accessible text labels; typography uses the system font stack.

Strings: sentence case; “Internet”, “LAN excluded”, “GB (decimal)”, “Kbps” and “Mbps” unambiguous. Never say “Connected” from saved configuration alone. Prefer “No fresh reading since 21:39.” Dates/times use Africa/Cairo including DST. MAC/IP in `<bdi dir="ltr">`; app layouts use logical margins, paddings and inset properties. See [screen patterns](03-screens.md#shared-interaction-and-content-contracts) for i18n and focus behavior.

Contrast measurements and remaining QA are recorded in [README](README.md#validation). This is a design target, not a full WCAG conformance claim.
