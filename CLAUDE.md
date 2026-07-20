# NetPilot

AI-first, open-source home network automation platform. First business feature: automatically apply per-device speed limits based on device category (Mobile, Television, IP Camera, Game Console, etc.), starting against a TP-Link Archer AX53, architected to support other router brands later.

## Status

Implementation is underway: all projects in `src/` exist and build (`NetPilot.Abstractions`, `NetPilot.Core`, `NetPilot.Data`, `TpLink.Sdk`, `NetPilot.Providers.TpLink`, `NetPilot.Agent`, `NetPilot.Web`), with tests under `test/`. Docker deploy assets (`deploy/docker/`) are in place and the app is **deployed and running** on the user's Proxmox home server (see Deployment section below). Confirm the plan still holds before writing code, but no need to re-litigate settled decisions unless something concrete conflicts with them.

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
- Deployment target: Docker Compose, two images (`Dockerfile.agent`, `Dockerfile.web`), one shared named volume. Runs on the user's Proxmox home server; the app itself has nothing Proxmox-specific, but see Deployment below for host-side gotchas.

## Deployment

Live on Proxmox at `192.168.1.13:8006`, inside LXC container **CT 109** (`NetPilot`, static IP `192.168.1.24` on `vmbr0`, same subnet as the router so the Agent can reach `192.168.1.1`).

- **CT must be privileged** (`unprivileged: 0` in `/etc/pve/lxc/109.conf`). Unprivileged LXC + nested Docker was tried and failed two ways: (1) runc can't write `net.ipv4.ip_unprivileged_port_start` under the unprivileged apparmor profile (`permission denied` on container start), and (2) even after adding `lxc.apparmor.profile: unconfined` and `features: nesting=1,keyctl=1`, flipping to `unprivileged: 1` killed `containerd` outright (nested overlayfs in a user namespace needs kernel/mount support this stack doesn't have). Don't re-attempt unprivileged without a concrete reason — privileged is the working, accepted state for this single-purpose home-lab host.
- CT config also carries `lxc.apparmor.profile: unconfined` and `features: nesting=1,keyctl=1` — keep both even though privileged, they were part of the working combination.
- Code is deployed via `git clone`/`git pull` of this repo directly onto the CT at `/opt/netpilot/repo`, then `docker compose --env-file .env up -d --build` from `deploy/docker/`. No CI/CD pipeline yet — updates are manual.
- Real router credentials live in `deploy/docker/.env` on the CT only (gitignored, never committed).

## Constraints

- The router lives at a private LAN address (`192.168.1.1`) — only reachable from the user's actual network, not from a cloud sandbox. Live protocol verification against the real device happens through the user's Cowork session (which has browser access), not here — if implementation surfaces a genuine protocol question that needs a live check, flag it back rather than guessing.
- Don't hand-roll the RSA/AES crypto without checking `phase1-live-findings.md` first — the login flow implemented there is simpler than the general TP-Link ecosystem docs suggest (confirmed: 2-call handshake, no AES envelope, no request signing for this firmware).
