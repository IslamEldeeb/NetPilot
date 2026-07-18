# NetPilot

AI-first, open-source home network automation platform. First business feature: automatically apply per-device speed limits based on device category (Mobile, Television, IP Camera, Game Console, etc.), starting against a TP-Link Archer AX53, architected to support other router brands later.

## Status

Architecture proposal is done and iterated on with the user; implementation has **not** started (no production code exists yet beyond the original empty project scaffold). Confirm the plan still holds before writing code, but no need to re-litigate settled decisions unless something concrete conflicts with them.

## Required reading before touching code

1. **`docs/mvp-product-architecture.md`** — the current blueprint. Project structure, domain model, the `IRouterProvider` multi-router abstraction, LiteDB persistence, Docker deployment. This is the spec.
2. **`docs/phase1-live-findings.md`** — confirmed-live protocol ground truth captured directly against the user's real router (login flow, exact `GetDevices`/`SetSpeedLimit` endpoints, field names, units). Implement `TpLink.Sdk` / `NetPilot.Providers.TpLink` against this, not guesses.

Background/historical, read only if needed: `docs/NetPilot_Research_Findings_and_Architecture.md` (earlier, now-superseded research doc — has extra protocol detail on non-Speed-Limit endpoints like wireless/VPN config if that's ever relevant), `docs/phase1-plan.md` (working log from initial repo scaffolding).

## Key decisions already made (don't re-derive these)

- Target framework: **.NET 10** (`net10.0`) across every project.
- Persistence: **LiteDB**, not EF Core/SQLite (embedded, no server, no migrations — deliberate for Docker deployment simplicity).
- `DeviceCategory` is **string/data-driven**, not a fixed enum — seed with the 13 categories confirmed live, but treat any new category a provider reports as valid (auto-create + log, don't drop).
- Router integration goes through `IRouterProvider` (`NetPilot.Abstractions`) — `NetPilot.Core` must never reference a concrete router SDK directly. `TpLink.Sdk` stays a standalone, protocol-only, independently publishable client; `NetPilot.Providers.TpLink` is the thin adapter.
- `NetPilot.Agent` (background worker) and `NetPilot.Web` (Blazor Server dashboard) are **separate deployables** sharing one LiteDB file — not merged into one process.
- Router password: encrypted at rest via ASP.NET Core Data Protection, shared key ring between Agent and Web. Never plaintext in the DB or committed to source control.
- Deployment target: Docker Compose, two images (`Dockerfile.agent`, `Dockerfile.web`), one shared named volume. Runs on the user's Proxmox home server, but nothing in the design is Proxmox-specific.

## Constraints

- The router lives at a private LAN address (`192.168.1.1`) — only reachable from the user's actual network, not from a cloud sandbox. Live protocol verification against the real device happens through the user's Cowork session (which has browser access), not here — if implementation surfaces a genuine protocol question that needs a live check, flag it back rather than guessing.
- Don't hand-roll the RSA/AES crypto without checking `phase1-live-findings.md` first — the login flow implemented there is simpler than the general TP-Link ecosystem docs suggest (confirmed: 2-call handshake, no AES envelope, no request signing for this firmware).
