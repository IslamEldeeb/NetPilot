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

The Settings → Backup button ships in a later UI batch. Until then, download with `curl` using your browser session. The dashboard login uses a cookie plus an antiforgery token; grab both from the login page form:

```bash
# 1. Fetch the login page and store the cookie + the hidden antiforgery token.
curl -c netpilot-cookies.txt https://<netpilot-host>/login -o login.html
TOKEN=$(grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' login.html | head -1 | sed 's/.*value="//;s/"//')

# 2. Sign in.
curl -b netpilot-cookies.txt -c netpilot-cookies.txt \
  -d "password=<dashboard-password>" \
  -d "__RequestVerificationToken=$TOKEN" \
  -L https://<netpilot-host>/login -o /dev/null

# 3. Download the backup (form post, antiforgery-validated).
curl -b netpilot-cookies.txt \
  -F "__RequestVerificationToken=$TOKEN" \
  -OJ https://<netpilot-host>/api/backup/download
```

If the dashboard login is disabled (no `NETPILOT_WEB_PASSWORD`), the endpoint needs no cookie and a plain form post is enough:

```bash
curl -X POST -d "" -OJ https://<netpilot-host>:8080/api/backup/download
```

The LiteDB file is copied, never locked, so it is safe to download while the Agent is running. Only one download runs at a time — retry if you get HTTP 429.

## Restoring a backup (manual, Docker)

The backup zip can only be restored while the containers are **stopped**, because a running process holds the database and key ring:

```bash
cd deploy/docker
docker compose stop

# Find the real volume name (Compose prefixes it with the project name).
VOLUME_NAME=$(docker inspect "$(docker compose ps -q netpilot-agent)" \
  --format '{{ range .Mounts }}{{ if eq .Destination "/data" }}{{ .Name }}{{ end }}{{ end }}')

docker run --rm -v "$VOLUME_NAME":/data -v "$(pwd)":/backup alpine \
  sh -c 'cd /data && rm -f netpilot.db netpilot.db-journal && unzip -o /backup/netpilot-backup-YYYYMMDD-HHmm.zip'

docker compose start
```

After the containers are back up, the dashboard should show the restored devices and policies, and the router password should decrypt from the restored key ring. A `manifest.json` mismatch between the `netpilot.db` and `keys/` entries means the zip was mixed from different backups — restore both from the same zip.

> **Warning:** the backup zip contains the Data Protection key ring. Treat it like a password: store it somewhere safe, never commit it, and delete old backups you no longer need.

A script-free fallback for containers without `unzip` is the tar-based approach in [deployment.md](deployment.md#backup-and-restore), but it captures the same two artifacts (`netpilot.db` + `keys/`) and remains valid when the web endpoint is unavailable (for example before the first UI batch).
