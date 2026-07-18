# NetPilot — Phase 1 Kickoff Plan

## Context

Co-worker delivered a research/architecture doc (`NetPilot_Research_Findings_and_Architecture.md`) for NetPilot, a .NET-based home network automation platform targeting the TP-Link Archer AX53. The doc reverse-engineers the router's auth protocol (RSA+AES handshake), confirms the "Speed Limit" bandwidth-control feature exists (distinct from the absent HomeShield QoS), and finds strong circumstantial evidence (from a sibling router's test fixtures) that the AX53's JSON API likely exposes per-device fields (`sl_enable`/`up_limit`/`down_limit`/`dev_type`/`qos_prior`) needed for the core business feature — but this is unconfirmed until tested against the live unit. No repo or code existed at plan time; this was pre-implementation.

Confirmed with user:
- Live router probing uses **Claude in Chrome** (extension, runs on user's real Chrome/LAN) — the in-app Browser tool was tried first and is sandboxed/remote, confirmed unable to reach private LAN addresses like `192.168.1.1`.
- Tether Management Protocol (port 20002, phase 4b) is **out of scope** for now — HTTPS cgi-bin API only.
- Repo at `/Users/ieldeeb/Projects/NetPilot`.

Environment check: dotnet SDK 10.0.201 available. User's other .NET repos (`PaymentService`, `StatementService`, `UserService` under `~/Projects/Github`) use a flat `<ProjectName>.sln` + `<ProjectName>.csproj` layout — this solution follows the same convention.

## Goal

Execute the doc's **Phase 1: Live verification** (§9) using Claude in Chrome, then scaffold the repo structure from §7 so Phase 2 (SDK implementation) can start clean.

## Steps

### 1. Repo scaffold (`/Users/ieldeeb/Projects/NetPilot`) — DONE
- `git init`
- `NetPilot.sln` with three projects:
  - `src/TpLink.Sdk/` (class library) — `Auth/`, `Session/`, `Transport/`, `Models/` folders + `TpLinkRouterClient.cs` stub. No `Tmp/` folder (TMP protocol out of scope).
  - `src/NetPilot.Core/` (class library) — `Devices/`, `Policy/`, `Enforcement/` folders. References `TpLink.Sdk`.
  - `src/NetPilot.Agent/` (worker service) — references both. Default `Worker.cs`.
  - `.gitignore` (standard .NET)
- `dotnet build` succeeds, 0 warnings/errors.
- No protocol logic written yet — structure only, so Phase 2 has a home to land in.

### 2. Live verification against the real AX53 (doc §9 Phase 1, items a–d; item e/TMP skipped) — DONE
Using Claude in Chrome (`mcp__claude-in-chrome__*`), user logged in themselves (Claude never handled the password):
- (a) Firmware confirmed: **Archer AX53 v1.0, 1.7.1 Build 20260213 rel.87654(4547)**.
- (b) The actual endpoint is **not** `admin/dhcps` — it's `admin/smart_network?form=game_accelerator` (`operation=loadDevice`/`loadSpeed`), plaintext JSON (no RSA/AES envelope). Full field list captured and mapped against the §6a hypothesis — all confirmed, camelCase names (`deviceType`, `enableLimit`, `downloadLimit`, `uploadLimit`, `enablePriority`, `timePeriod`, `deviceTag`, `speedLimitOnline`).
- (c) Speed Limit edit modal (Network Map → Clients → pencil icon) confirmed reading from the same already-fetched device list, no separate per-device GET. Write operation name NOT captured (would require changing a real device's live setting — deferred, see live-findings doc "Open items").
- (d) JS bundles searched: found `qos`/`qosAdv` i18n key groups confirming a Priority/schedule feature exists in code; live-navigated to HomeShield → Network Analysis & Optimization and confirmed "Quality of Service" is listed but is a Tether-app/subscription marketing page only, no actionable Web UI controls — matches original doc research exactly.

**Full detail:** `docs/phase1-live-findings.md`.

### 3. Write up findings — DONE
See `docs/phase1-live-findings.md`. Bottom line: the §6a hypothesis was correct in concept and TP-Link's device-type classification is better than expected (human-readable categories that already match the business requirement). The traffic-shaping bridge fallback is very likely unnecessary.

**Update:** with the user's explicit go-ahead, also captured the write path live (resubmitted an existing device's current values — zero-risk, device left unchanged, confirmed via UI afterward). Read and write use *different* `form` values on the same `smart_network` section: reads via `form=game_accelerator`, writes via `form=client_speed_limit` (`operation=write&mac=..&enableLimit=on|off&downloadLimit=..&uploadLimit=..` → `{"success":true}`). `TpLinkSpeedLimitEnforcer` now has a complete, live-verified contract — Phase 2 can implement directly against it, no further router probing needed to start.

## Out of scope for this pass
- Any actual SDK/business logic implementation (Phase 2+ in doc §9) — after Phase 1 findings are in hand.
- TMP/Tether protocol client (doc §4b, §6b) — explicitly deferred.
- Traffic-shaping bridge fallback — only scoped if live probing shows the §6a fields don't exist.

## Verification
- `dotnet build` on the solution succeeds — confirmed.
- `git status` shows a clean initial commit-able tree — confirmed, not yet committed (only commit when user asks).
- Phase 1 findings doc contains a definitive yes/no on the §6a field hypothesis, with the actual JSON observed (or absence thereof) — pending extension connection.
