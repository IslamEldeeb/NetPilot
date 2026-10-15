# Deploying NetPilot with Docker

This guide covers deploying NetPilot with Docker Compose on any Docker host — a Linux server, a VM, an LXC container, or a NAS with Docker support. The Docker host must reach your router's LAN address. The current deployment is privileged Proxmox LXC CT 109 (`192.168.1.24`), with manual updates from `/opt/netpilot/repo/deploy/docker`; host-specific constraints are recorded in `CLAUDE.md`.

## Prerequisites

- A Docker host with **Docker Engine** and the **Compose plugin** (`docker compose`, not the standalone `docker-compose` v1).
- Network reachability from the Docker host to your router's admin address (e.g. `192.168.1.1`) — the containers only make outbound HTTP calls to the router, so standard Docker bridge networking is enough. No `host` or `macvlan` network mode is needed.
- Your router's admin password.

## What gets deployed

Two images, built from this repo, sharing one named volume:

```
deploy/docker/
├── Dockerfile.agent       multi-stage build → NetPilot.Agent (background worker)
├── Dockerfile.web         multi-stage build → NetPilot.Web (dashboard, port 8080)
├── docker-compose.yml     both services + shared volume
├── docker-compose.tailscale.yml  optional private HTTPS, no TUN or host ports
├── tailscale-serve.json    HTTPS proxy to netpilot-web:8080
├── docker-compose.caddy.yml      optional LAN-only HTTPS alternative
├── Caddyfile              internal CA for 192.168.1.24
└── .env.example           router seed, notification contact, timezone, Tailscale
```

| Service | Image built from | Exposes | Role |
|---|---|---|---|
| `netpilot-agent` | `Dockerfile.agent` | — (no published port) | Runs the reconciliation loop against your router |
| `netpilot-web` | `Dockerfile.web` | `8080` | Dashboard UI |

Both containers mount the same `netpilot-data` volume, which holds the LiteDB file (`netpilot.db`) and the shared ASP.NET Core Data Protection key ring (`keys/`) — this is what lets either process decrypt a router password the other one encrypted, and what makes the dashboard and Agent see the same devices/policies.

## First-time deployment

1. **Clone the repository** onto the Docker host:

   ```bash
   git clone <this-repo-url> netpilot
   cd netpilot
   ```

2. **Create your environment file** from the example:

   ```bash
   cp deploy/docker/.env.example deploy/docker/.env
   ```

   Edit `deploy/docker/.env`:

   ```dotenv
   ROUTER_HOST=192.168.1.1
   ROUTER_PASSWORD=your-router-admin-password
   NETPILOT_VAPID_SUBJECT=mailto:you@example.com
   NETPILOT_TIMEZONE=Africa/Cairo
   ```

   Replace `you@example.com` with your real monitored contact email before enabling push.

   This file is gitignored — never commit real credentials. `ROUTER_HOST`/`ROUTER_PASSWORD` are a **first-run convenience only**: they seed the stored router connection if it's empty. Once a connection exists (seeded, or entered through the dashboard), these variables are ignored — the dashboard's connection panel is the source of truth from then on. Changing these two seed variables later does not update the stored router connection; edit that connection in the dashboard. Other environment settings below do take effect when Compose recreates the services. Do not clear the volume to change credentials.

3. **Build and start both services**:

   ```bash
   cd deploy/docker
   docker compose --env-file .env up -d --build
   ```

4. **Verify both containers are healthy**:

   ```bash
   docker compose ps
   docker compose logs -f netpilot-agent
   ```

   Look for `Connected to router at <host>` in the Agent's logs. If instead you see `No router configured yet...`, double-check `ROUTER_HOST`/`ROUTER_PASSWORD` in `.env` and that the container can reach the router (see Troubleshooting below).

5. **Open the dashboard** at `http://<docker-host-ip>:8080`. This LAN URL stays plain HTTP; Android push needs a trusted HTTPS origin.

6. **Optional private HTTPS:** follow [HTTPS access](https-access.md) to enroll Tailscale and configure your phone, using this command instead of the base-only `up`:

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.tailscale.yml --env-file .env up -d --build
   ```

   The userspace container needs no TUN passthrough or LXC config changes and publishes no new host ports. It adds a private `https://<name>.<tailnet>.ts.net` URL. Keep using both files on updates; never enable Funnel or expose the dashboard publicly. The LAN-only Caddy alternative uses `docker-compose.caddy.yml` as the second file and requires installing its CA on the phone.

## Configuration reference

| Variable | Where | Default | Purpose |
|---|---|---|---|
| `ROUTER_HOST` | `deploy/docker/.env` | — | Router LAN address, first-run seed only |
| `ROUTER_PASSWORD` | `deploy/docker/.env` | — | Router admin password, first-run seed only, encrypted before storage |
| `NetPilot__DataDirectory` | Baked into both Dockerfiles | `/data` | Where the shared LiteDB file and key ring live inside each container |
| `NetPilot__PollIntervalSeconds` | Agent's `appsettings.json`, override via compose `environment:` if needed | `180` | Reconciliation tick interval |
| `NETPILOT_VAPID_SUBJECT` | `.env` → both services' `NetPilot__Notifications__VapidSubject` | `mailto:netpilot@localhost` | Placeholder fallback; set a real monitored `mailto:` contact for Web Push |
| `NETPILOT_TIMEZONE` | `.env` → both services' `NetPilot__TimeZone` | `Africa/Cairo` | Optional IANA zone for usage periods and notification quiet hours; use the same zone in both hosts |
| `TS_AUTHKEY` | `.env` → Tailscale override only | Empty | Secret enrollment key; short expiry, tagged, preferably one-off; clear after enrollment |
| `TS_HOSTNAME` | `.env` → Tailscale override only | `netpilot` | Requested machine name; check actual HTTPS DNS name after enrollment |
| `ASPNETCORE_URLS` | Baked into `Dockerfile.web` | `http://+:8080` | Web listen address |

To override any `NetPilot:*` setting without rebuilding, add it under `environment:` in `docker-compose.yml` using the double-underscore convention (e.g. `NetPilot__PollIntervalSeconds=60`).

## Quotas start in dry-run

New quota policies default to **Dry-run on**. They evaluate thresholds and report events without applying quota Throttle/Block writes. This setting is per policy, not a global kill switch for existing speed policies or previously applied actions. Existing saved quota choices survive upgrades; check them in **Quotas** after deployment. Protect your management phone before testing.

Keep enforcement policies in dry-run while comparing usage with the router for several days. The router currently counts LAN traffic too; [T8 LAN-traffic exclusion](phase5-quotas-notifications-rules-plan.md#9-t8--exclude-lan-traffic-from-internet-usage-jellyfin--tv) is still planned and must land before enabling automatic Throttle/Block. A protected-device `NotifyOnly` delivery test below is the limited exception; it makes no quota router writes.

## Upgrading

```bash
cd netpilot
git pull
cd deploy/docker
docker compose --env-file .env up -d --build
```

With Tailscale enabled, replace the last command above with:

```bash
docker compose -f docker-compose.yml -f docker-compose.tailscale.yml --env-file .env up -d --build
```

Use the Caddy override instead if that is your selected HTTPS option. `--build` is required to rebuild local source when `image:` tags are present. Preserve `tailscale-state` or `caddy-data`/`caddy-config` alongside the application volume; do not run `down -v` during updates.

The `netpilot-data` volume is untouched by a rebuild — devices, policies, and activity history persist across upgrades. There are no database migrations to run; LiteDB just opens the existing file.

## Post-deploy verification

Use the same Compose file set as your deployment for `ps`, `logs` and `exec` (add both `-f` arguments when using HTTPS).

- [ ] Services stay running; Agent logs show router connection and subsequent ticks without exceptions. Check Web and HTTPS proxy logs as well.
- [ ] Dashboard loads; **Quotas** and **Notifications** tabs render. Confirm the configured timezone and VAPID contact.
- [ ] On the Android phone, open the selected trusted HTTPS URL. Notifications shows **Secure context: Yes** and **Push supported: Yes**. Enable notifications, allow permission, and confirm **Subscribed here: Yes**.
- [ ] Send a test notification. Check the per-subscription result **and actual phone display**; acceptance by the push service alone does not prove delivery.
- [ ] In **Quotas → Protected devices**, protect the phone's MAC. Create a fresh per-device policy for it: **Action = NotifyOnly**, **Exempt off**, **Dry-run off**, daily cap **0.001 GB** (decimal 1 MB), monthly cap empty. Set this before its first evaluated crossing: threshold events are remembered once per period, so editing an already-crossed dry-run policy does not replay the exceeded event. Use a device/period with no prior crossing state or wait for the next day rather than deleting database rows.
- [ ] Generate a small amount of phone traffic; wait for the Agent's next tick (default 180 seconds). Check the exceeded event in Activity/Notifications and the phone notification. The phone remains protected; NotifyOnly never throttles or blocks it. Restore/remove this temporary device policy afterwards, checking what it will inherit.
- [ ] Keep **every other quota policy in dry-run** until usage agrees with the router's own numbers over several days, units/reset behaviour are understood, and T8 excludes LAN traffic. Do not test live Block/Throttle as part of this rollout.

## Backup and restore

The entire application state is one LiteDB file plus a small key ring, both on the `netpilot-data` volume — but Compose prefixes volume names with the project name (usually the directory `docker compose` was run from), so the real volume is unlikely to be named literally `netpilot-data`. Look it up rather than guessing:

```bash
cd deploy/docker
VOLUME_NAME=$(docker inspect "$(docker compose ps -q netpilot-agent)" \
  --format '{{ range .Mounts }}{{ if eq .Destination "/data" }}{{ .Name }}{{ end }}{{ end }}')
echo "$VOLUME_NAME"   # sanity-check this is non-empty before continuing
```

**Backup:**

```bash
docker run --rm -v "$VOLUME_NAME":/data -v "$(pwd)":/backup alpine \
  tar czf /backup/netpilot-backup.tar.gz -C /data .
```

**Restore** (into a fresh volume, before first `up`):

```bash
docker run --rm -v "$VOLUME_NAME":/data -v "$(pwd)":/backup alpine \
  tar xzf /backup/netpilot-backup.tar.gz -C /data
```

**Important:** if you restore the LiteDB file without restoring the matching `keys/` directory alongside it, the stored router password can no longer be decrypted — you'll need to re-enter it from the dashboard. Always back up and restore both together.

## Logs

Both services log to `json-file` with rotation already configured (`max-size: 10m`, `max-file: 3`):

```bash
docker compose logs -f netpilot-agent
docker compose logs -f netpilot-web
```

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Agent logs `No router configured yet...` forever | `ROUTER_HOST`/`ROUTER_PASSWORD` not set (or typoed) in `.env`, or the Docker host can't route to the router's LAN address |
| Login rejected / `Login rejected — check password, or another session may hold the router's single login slot` | Wrong password, or someone is logged into the router's admin UI in a browser at the same time — this firmware allows exactly one active session |
| Dashboard shows devices but limits never change | Check that the category's policy is actually saved (`IsUserConfigured`) — a never-seen category is auto-created as `Unlimited` but is **not** pushed to the router until a human edits and saves it from the dashboard, by design |
| HTTPS errors talking to the router | The router's self-signed certificate is trusted only for the exact configured host — confirm `ROUTER_HOST` matches what you actually browse to, or set `UseHttps` off if your router serves plain HTTP |

## Security

The dashboard has **no built-in authentication**: anyone who can reach it can change settings. Keep access on your private LAN or your own trusted tailnet. **Never expose NetPilot to the public Internet**, forward ports `8080`/`443` from the router, or enable Tailscale Funnel. [Tailscale Serve](https-access.md) provides private HTTPS for Android push and remote access; HTTPS itself does not add a login. LAN access at `:8080` remains plain HTTP.
