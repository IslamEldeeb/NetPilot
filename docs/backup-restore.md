# Backup and restore (T20a)

NetPilot's entire application state lives on the shared `netpilot-data` Docker volume:

- `netpilot.db` — the LiteDB file: devices, policies, quotas, rules, activity, the encrypted router connection.
- `keys/` — the ASP.NET Core Data Protection key ring: what lets Web and Agent decrypt the router password (and what push notifications need).

Both parts are required. A database restored without its key ring can no longer decrypt the stored router password — you would have to re-enter it from the dashboard. Back them up and restore them together.

## What the backup contains

`POST /api/backup/download` streams a zip (`application/zip`, `Cache-Control: no-store`) named `netpilot-backup-YYYYMMDD-HHmm.zip` (household timezone, Africa/Cairo by default):

- `netpilot.db` — copied through a shared-read handle while the Agent and Web keep using the live file, so a backup never locks or corrupts a running database.
- `keys/*` — every file of the Data Protection key ring.
- `manifest.json` — backup format version, creation time, app version, and for every backed-up file its size and SHA-256, so a restore can be verified.

The endpoint requires a signed-in dashboard session (it is not on the login allow-list), validates antiforgery like every other form post, and allows one concurrent download (a second concurrent request gets HTTP 429).

## Downloading a backup

Open **Settings → Backup** (`/settings/backup`) and select **Download backup**. The browser submits a normal form POST with a fresh antiforgery token and downloads the archive. A signed-in session is required when dashboard login is enabled. Antiforgery is required even when login is disabled; a plain tokenless `curl` POST is rejected. Retry after 30 seconds if another download is in progress.

The shared retention editor on the same page controls notification attempts, inactive quota-state days (minimum 62), and firing-history limits. Both hosts read the shared settings. Active claims, active quota actions, pending-claim restoration metadata, active rule firings and firing watermarks are retained. These controls do not prune usage/activity history or delete downloaded backup archives. Restore remains manual (D8), without an in-app upload or restart handshake.

A successful download and matching manifest checksums do not prove a successful restore. Rehearse on an isolated copied volume before relying on a backup for recovery.

## Restoring a backup (manual, Docker)

The backup zip can only be restored while the containers are **stopped**, because a running process holds the database and key ring:

```bash
cd deploy/docker
docker compose stop

# Find the real volume name (Compose prefixes it with the project name).
VOLUME_NAME=$(docker inspect "$(docker compose ps -a -q netpilot-agent)" \
  --format '{{ range .Mounts }}{{ if eq .Destination "/data" }}{{ .Name }}{{ end }}{{ end }}')

docker run --rm -v "$VOLUME_NAME":/data -v "$(pwd)":/backup alpine \
  sh -c 'cd /data && rm -f netpilot.db netpilot.db-journal && unzip -o /backup/netpilot-backup-YYYYMMDD-HHmm.zip'

docker compose start
```

After the containers are back up, the dashboard should show the restored devices and policies, and the router password should decrypt from the restored key ring. A `manifest.json` mismatch between the `netpilot.db` and `keys/` entries means the zip was mixed from different backups — restore both from the same zip.

> **Warning:** the backup zip contains the Data Protection key ring. Treat it like a password: store it somewhere safe, never commit it, and delete old backups you no longer need.

A script-free fallback for containers without `unzip` is the tar-based approach in [deployment.md](deployment.md#backup-and-restore), but it captures the same two artifacts (`netpilot.db` + `keys/`) and remains valid when the web endpoint is unavailable (for example before the first UI batch).
