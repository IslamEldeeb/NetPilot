# Deploying NetPilot with Docker

This guide covers deploying NetPilot with Docker Compose on any Docker host — a Linux server, a VM, an LXC container, or a NAS with Docker support. Nothing here is specific to any particular hypervisor or hosting setup; the only real requirement is that the Docker host can reach your router's LAN address.

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
└── .env.example           ROUTER_HOST / ROUTER_PASSWORD (first-run seed only)
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
   ```

   This file is gitignored — never commit real credentials. `ROUTER_HOST`/`ROUTER_PASSWORD` are a **first-run convenience only**: they seed the stored router connection if it's empty. Once a connection exists (seeded, or entered through the dashboard), these variables are ignored — the dashboard's connection panel is the source of truth from then on. Editing `.env` after the first run has no effect unless you also clear the volume.

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

5. **Open the dashboard** at `http://<docker-host-ip>:8080`.

## Configuration reference

| Variable | Where | Default | Purpose |
|---|---|---|---|
| `ROUTER_HOST` | `deploy/docker/.env` | — | Router LAN address, first-run seed only |
| `ROUTER_PASSWORD` | `deploy/docker/.env` | — | Router admin password, first-run seed only, encrypted before storage |
| `NetPilot__DataDirectory` | Baked into both Dockerfiles | `/data` | Where the shared LiteDB file and key ring live inside each container |
| `NetPilot__PollIntervalSeconds` | Agent's `appsettings.json`, override via compose `environment:` if needed | `180` | Reconciliation tick interval |
| `ASPNETCORE_URLS` | Baked into `Dockerfile.web` | `http://+:8080` | Web listen address |

To override any `NetPilot:*` setting without rebuilding, add it under `environment:` in `docker-compose.yml` using the double-underscore convention (e.g. `NetPilot__PollIntervalSeconds=60`).

## Upgrading

```bash
cd netpilot
git pull
cd deploy/docker
docker compose --env-file .env up -d --build
```

The `netpilot-data` volume is untouched by a rebuild — devices, policies, and activity history persist across upgrades. There are no database migrations to run; LiteDB just opens the existing file.

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

The dashboard has no built-in authentication and assumes LAN-only reachability. **Do not publish port `8080` to the public internet.** If you need remote access, put it behind a VPN (e.g. WireGuard/Tailscale) or a reverse proxy that adds its own authentication — don't rely on NetPilot itself for that in v1.
