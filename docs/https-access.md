# HTTPS access for Android Web Push

Android Chrome requires a **secure context** for service workers and Web Push: use trusted HTTPS. Plain `http://192.168.1.24:8080` does not qualify. The local-development exception is localhost on the device running Chrome: desktop `http://localhost:5036` works; localhost on a phone means the phone, not CT 109. See [Chromium's secure-origin guidance](https://www.chromium.org/Home/chromium-security/deprecating-powerful-features-on-insecure-origins/).

**For the existing Proxmox setup, use the Cloudflare + nginx subdomain route below. Set `NETPILOT_WEB_PASSWORD` whenever the subdomain is public.** HTTPS and scoped notification tokens alone do not protect dashboard settings.

| Option | Use when | Trade-off |
|---|---|---|
| **Cloudflare + nginx (primary)** | One public subdomain per Proxmox CT, existing nginx and Cloudflare certificates | Requires dashboard password and restricted upstream port |
| **A. Tailscale Serve (alternative)** | You prefer private access through a tailnet | Requires Tailscale on the phone |
| **B. Caddy internal CA (alternative)** | Access stays on the home LAN | Install a private CA on each client |
| **C. Chrome insecure-origin flag** | One-off testing only | Insecure HTTP; remove after testing |

## Dashboard login

Set a long random `NETPILOT_WEB_PASSWORD` in `deploy/docker/.env`, then recreate Web with `docker compose --env-file .env up -d --build netpilot-web`. Compose passes it **only to Web** as `NetPilot__Web__Password` (`NetPilot:Web:Password`). Empty/unset disables login and preserves existing local behavior. Startup logs say `Dashboard login enabled` or `Dashboard login disabled`; the password/hash is never logged by login code. Keep `.env` private and out of Git.

Login uses one shared identity and a sliding 30-day HttpOnly, SameSite=Lax cookie. `/login` accepts only local root-relative return URLs. Wrong passwords show **Wrong password**; POSTs require antiforgery tokens and login permits five attempts per minute per forwarded IP (429 after that). `/logout` offers an antiforgery-protected Sign out form. Changing the password does not revoke already issued cookies; this simple shared-password design has no session database. Closing a tab or signing out does not forcibly terminate an already open circuit in another tab; refresh/close other dashboard tabs after signing out.

### Primary route — Cloudflare + nginx subdomain

Point the Cloudflare-proxied subdomain (for example `netpilot.example.com`) at the existing nginx CT. Use Cloudflare **Full (strict)** TLS and configure nginx's HTTPS virtual host for that name with the existing Cloudflare origin certificate/key. The browser uses Cloudflare's trusted edge certificate; the origin certificate belongs on nginx. No Tailscale is needed. Keep Cloudflare WebSockets enabled and bypass caching for this subdomain, including HTML/login responses. Optionally add Cloudflare Access as another access layer; check service-worker notification actions if you do.

Inside nginx's `http` block, define:

```nginx
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}
```

Inside the subdomain's HTTPS `server` block (existing certificate directives omitted), proxy the entire app, including `/_blazor`:

```nginx
location / {
    proxy_pass http://192.168.1.24:8080;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $connection_upgrade;
    proxy_read_timeout 3600s;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-Host $host;
    proxy_buffering off;
}
```

`$remote_addr` must represent the client: on a Cloudflare-proxied vhost configure nginx's real-IP module with `real_ip_header CF-Connecting-IP` and `set_real_ip_from` for **Cloudflare's current published IP ranges only**. Restrict origin ingress to Cloudflare, and never trust arbitrary client-supplied forwarding headers. Without that real-IP setup, the limiter groups users by Cloudflare edge address. nginx overwrites X-Forwarded-For with that resolved address, so the app only needs one trusted forwarding hop. See [Cloudflare visitor IP restoration](https://developers.cloudflare.com/support/troubleshooting/restoring-visitor-ips/restoring-original-visitor-ips/) and [nginx WebSocket proxying](https://nginx.org/en/docs/http/websocket.html).

**Never expose the Web port directly to the internet.** When login is enabled, Web deliberately trusts one `X-Forwarded-For`/`X-Forwarded-Proto` hop (`ForwardLimit=1`, cleared known proxy/network lists). Restrict CT TCP 8080 with Proxmox/host firewall rules to the nginx CT and trusted management clients, or bind it only to a private proxy network. Keep router forwarding to 8080 disabled. Only nginx should supply these headers. Forwarded HTTPS makes the SameAsRequest auth cookie Secure even though nginx talks HTTP upstream. With login disabled this task does not enable forwarded-header handling.

Check nginx config with `nginx -t`, reload nginx, open the HTTPS subdomain on the phone, sign in, and exercise a dashboard control to confirm Blazor is interactive. Repeat the notification steps in Option A step 7 using this subdomain. Verify an anonymous browser redirects to `/login`, anonymous API/Blazor requests get 401, `/sw.js` remains available, and signed notification actions still work after cookie expiry. The public allow-list is GET/HEAD `/login`, `/logout`, `/sw.js`, `/manifest.webmanifest`, `/icons/*`, `/favicon.png`, `/app.css`, `/lib/bootstrap/dist/css/bootstrap.min.css`; POST `/login`, `/logout`, `/api/notifications/action`. All other assets and `/_blazor` require login.

These deployment recipes were checked against upstream documentation and Compose configuration. Certificate issuance, Android trust, subscription and delivery are **expected behaviour**, not live-verified here. Complete the [post-deploy checklist](deployment.md#post-deploy-verification) on CT 109 and the phone.

## Option A — Tailscale Serve (alternative)

The current server is privileged Proxmox LXC **CT 109**, `192.168.1.24`, with the repo at `/opt/netpilot/repo`. The override runs the official Tailscale image in **userspace networking** (`TS_USERSPACE=true`), needing neither `/dev/net/tun` passthrough nor extra container capabilities. It joins the same Compose network as Web and proxies to `http://netpilot-web:8080`; `localhost:8080` would point at the Tailscale container itself.

1. Create a [Tailscale account/tailnet](https://tailscale.com/start). Use a private tailnet containing only your trusted devices. In the admin console's **DNS** page, enable **MagicDNS** and **HTTPS certificates**, accepting the certificate-name disclosure notice. Use the full `<name>.<tailnet>.ts.net` name for HTTPS. Certificate names are public in certificate-transparency logs; that does not make the dashboard public. See [Enabling HTTPS](https://tailscale.com/docs/how-to/set-up-https-certificates).

2. In **Access controls**, define a server tag such as `tag:netpilot` with your administrator as tag owner. Ensure your own user/devices can reach that tag on TCP 443; restrict other tailnet members if present. A tag alone does not restrict access. See [Tailscale tags](https://tailscale.com/docs/features/tags).

3. In **Settings → Keys → Generate auth key**, create a **tagged** key for `tag:netpilot`. Prefer **one-off** for this single persistent server; choose reusable only if you need repeated enrollment. Set a short expiry, such as one day; leave **Ephemeral off** so the server identity persists. Approve the device after enrollment if device approval is enabled, or pre-approve the key. Keep the key private. [Auth-key guidance](https://tailscale.com/docs/features/access-control/auth-keys).

4. On CT 109, create `.env` from `.env.example` if it does not already exist. Edit the existing file rather than overwriting router settings:

   ```bash
   cd /opt/netpilot/repo/deploy/docker
   # Create only if absent; preserve an existing .env:
   test -f .env || cp .env.example .env
   chmod 600 .env
   ```

   Set `TS_AUTHKEY` to your enrollment key and `TS_HOSTNAME=netpilot` (or a unique machine name). Set `NETPILOT_VAPID_SUBJECT` to a real monitored `mailto:` contact. `.env` is gitignored; never commit it or share rendered Compose output containing it.

5. Start with **both** Compose files:

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.tailscale.yml --env-file .env up -d --build
   docker compose -f docker-compose.yml -f docker-compose.tailscale.yml --env-file .env logs --tail=100 tailscale
   docker compose -f docker-compose.yml -f docker-compose.tailscale.yml --env-file .env exec tailscale tailscale serve status
   ```

   Find the resulting `https://netpilot.<tailnet>.ts.net` URL in Serve status or the machine's DNS name in the admin console. Copy the actual name; duplicates can change it. No extra host ports are published. The existing `http://192.168.1.24:8080` LAN URL remains **plain HTTP** and cannot enable push.

6. Install the official Tailscale Android app, sign in to the **same tailnet** as your own user, and connect (accept Android's VPN prompt). Open the full HTTPS URL in a normal Chrome tab, without `:8080`. Keep Tailscale connected at home and away when opening the dashboard or using notification actions. No subnet router or exit node is required for this URL.

7. Open **Notifications**. Check **Secure context: Yes**, **Push supported: Yes**. Select **Enable notifications on this device**, allow Chrome's prompt, then verify permission is `granted` and **Subscribed here: Yes**. Select **Send test notification** and check the phone actually displays it. PWA installation is optional. See [server notifications](server-notifications.md) for delivery results and action behaviour.

The dedicated `tailscale-state` volume stores the server identity; preserve it across updates. `TS_AUTH_ONCE=true` reuses that identity rather than enrolling on every restart. After successful enrollment, clear `TS_AUTHKEY` in `.env` and revoke any reusable enrollment key you no longer need; an enrolled node remains authorized separately. If state is lost or the node needs reauthorization, supply a fresh key. Do not use `docker compose down -v` for an update. See [Docker parameters](https://tailscale.com/docs/features/containers/docker/docker-params).

`tailscale-serve.json` contains a literal `${TS_CERT_DOMAIN}` placeholder, replaced by Tailscale's container entrypoint, not Compose. Funnel is explicitly disabled. The config is mounted as a single read-only file, so after changing it use the same two-file command with `up -d --build --force-recreate tailscale`; do not rely on hot reload. Upstream recommends directory mounts for live config watching; this mount avoids exposing the adjacent secret `.env` file.

The pin is **`tailscale/tailscale:v1.104.1`**, the container release listed on 2026-10-13 in the [official changelog](https://tailscale.com/changelog). Bump `image:` in `deploy/docker/docker-compose.tailscale.yml` after reviewing releases, then pull/recreate and repeat the phone checks. Use both `-f` arguments on every update or service-management command for this deployment.

### Troubleshooting Tailscale and push

- **Certificate not issued yet / TLS error:** inspect `tailscale` logs and Serve status; allow the first certificate request to complete and retry. Confirm the CT has outbound Internet and correct time. Repeated enrollment/certificate requests can hit issuance limits; preserve state and follow logged retry guidance instead of repeatedly recreating identities.
- **No HTTPS endpoint:** check both MagicDNS and HTTPS certificates in the admin console, device approval, and the full DNS name. Do not substitute the LAN IP, bare `netpilot`, or a Tailscale IP into the HTTPS URL.
- **Phone cannot open the URL:** check Tailscale is connected to the same tailnet, the server is online, and access rules permit your phone/user to reach `tag:netpilot:443`.
- **Proxy error:** check `netpilot-web` is running and Web logs are clean. The upstream must be `http://netpilot-web:8080` on the shared Compose network. No host port forwarding is needed.
- **Secure context: No:** you opened the HTTP LAN URL. Open the trusted HTTPS URL instead; do not bypass certificate errors.
- **Permission denied:** Chrome's icon beside the address → **Site settings → Notifications → Allow**, then refresh and enable again. Permission and subscriptions belong to each origin; changing HTTPS names requires enabling again.
- **Push service accepted but no notification:** Android **Settings → Apps → Chrome → Notifications** must allow notifications, including the site's notification channel. Check Do Not Disturb and battery optimisation/background restrictions for Chrome and Tailscale. Menu labels vary by Android/vendor. Check the Notifications tab's per-device results; acceptance alone does not prove display. Agent/Web need outbound Internet for push-service sends.

## Option B — LAN-only Caddy internal CA (alternative)

Use `docker-compose.caddy.yml` and `Caddyfile` instead of the Tailscale override. The shipped recipe targets the current CT IP **192.168.1.24**, binding host TCP 443 only on that LAN address. If the CT address changes, update **both** the port binding and Caddyfile site address before starting. Keep router port forwarding disabled. This option works only on the home network; it does not provide remote access.

```bash
cd /opt/netpilot/repo/deploy/docker
docker compose -f docker-compose.yml -f docker-compose.caddy.yml --env-file .env up -d --build
docker compose -f docker-compose.yml -f docker-compose.caddy.yml --env-file .env logs --tail=100 caddy
docker compose -f docker-compose.yml -f docker-compose.caddy.yml --env-file .env cp caddy:/data/caddy/pki/authorities/local/root.crt /tmp/netpilot-caddy-root.crt
```

The Caddyfile uses `tls internal` and proxies to `netpilot-web:8080`, including Blazor's WebSocket connection. The pinned official image is **`caddy:2.11.7-alpine`**; review [Caddy releases](https://github.com/caddyserver/caddy/releases) and the [official image tags](https://hub.docker.com/_/caddy) before bumping `image:` in `docker-compose.caddy.yml`. See [Caddy local TLS](https://caddyserver.com/docs/caddyfile/directives/tls) and [Docker CA export](https://caddyserver.com/docs/running#docker-compose).

Transfer **only `root.crt`**, using a trusted method such as USB or an authenticated file transfer, to the Android phone. Do not transfer Caddy's CA private key or its entire data directory. On Android, open **Settings → Security → Encryption & credentials → Install a certificate → CA certificate**, select the exported file, and confirm with the device PIN. Menu names vary; see [Android certificate settings](https://support.google.com/pixelphone/answer/2844832?hl=en).

Android warns that a CA can inspect encrypted traffic; install only the root exported from your own Caddy. It may show a persistent network-monitoring warning. Restart Chrome, open **`https://192.168.1.24`** (no `:8080`), and verify it loads **without a certificate warning**, then follow Option A step 7. Clicking through a certificate warning is not a trusted-HTTPS setup. If this Android/Chrome build does not trust the user-installed CA, use the primary subdomain route or Option A.

After editing `Caddyfile`, recreate Caddy to load it:

```bash
docker compose -f docker-compose.yml -f docker-compose.caddy.yml --env-file .env up -d --force-recreate caddy
```

Preserve **`caddy-data`**, which holds the CA and private keys, and `caddy-config` across updates. Losing Caddy data creates a new CA: remove the old root from the phone, export the new root and install it again. Protect backups of the CA private key. The original LAN `:8080` URL remains HTTP. This override and CA procedure are reviewed configuration with **expected behaviour**; Caddy startup, Android menu flow and Chrome trust have not been verified on this phone.

## Option C — insecure one-off test only

**This does not encrypt HTTP and must never be the long-term setup.** If available in your Android Chrome version:

1. Open `chrome://flags/#unsafely-treat-insecure-origin-as-secure`.
2. Add exactly `http://192.168.1.24:8080`, enable the flag and relaunch Chrome.
3. Open that URL, check **Secure context: Yes**, enable notifications and send a test.
4. Afterwards remove the origin, return the flag to **Default** and relaunch. Move to Option A or B and enable notifications on that HTTPS origin.

The flag may be absent or behave differently in some Chrome versions or managed devices. This is **expected behaviour**, not a verified phone test; if unavailable, use HTTPS. Never expose the plain HTTP Web port publicly.
