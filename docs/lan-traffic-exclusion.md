# LAN traffic exclusion (T8)

Implemented; **expected behaviour, unverified live**. Leave quota enforcement in dry-run
until the bridge counters and the router's byte units/counting have been compared on the
actual network. No URL and no LAN-only flags means exactly the previous usage numbers.

## What is counted

The router's `trafficUsage` counts LAN traffic as well as Internet traffic, including a
Jellyfin stream on both server and TV. NetPilot optionally reads cumulative directional
LAN counters from an external HTTP exporter and maps flow endpoints to device snapshot IPs.

`Internet delta = max(0, router delta − LAN delta − saved LAN carry)`

A flow is attributed to **both** known endpoints, or just the known endpoint if its peer
is missing from the router snapshot. Both directions are summed. For example, after the
initial flow baseline, a 2 GB Jellyfin stream plus 0.3 GB YouTube on the TV produces:

| Device | Router bytes | LAN excluded | Internet bytes |
|---|---:|---:|---:|
| Jellyfin server | 2 GB | 2 GB | 0 GB |
| TV | 2.3 GB | 2 GB | 0.3 GB |

These are decimal GB. The Usage tab retains its existing binary byte formatting. LAN
network totals count both endpoints (4 GB above); they are not unique bytes on the wire.

**`DeviceUsageState.CurrentDayBytes` / `CurrentMonthBytes`, and history `TotalBytes`, now
mean Internet bytes. Quotas, future rules and notifications keep consuming these fields
unchanged.** Existing accumulated usage is not retroactively reclassified. New LAN history
fields default to zero when opening older databases. Devices badges label Internet usage;
Usage shows Internet and, when positive, excluded LAN totals/columns. Quotas → Usage
exclusions can mark a device LAN-only.

The router's first-ever reading still counts in full and its numeric reset logic is
unchanged. A first-ever **flow** reading is baseline-only because exporters may be
long-lived. A lower flow counter counts its new value as the delta and emits one
`LanCounterReset` activity entry; an unchanged counter emits none. Baselines persist in
`lan_flow_state`, including temporarily absent pairs. Source health persists separately in
`lan_source_status`, shared by Agent and Web.

If LAN arrives before router bytes, the excess becomes `PendingLanCarryBytes`, capped at
**1,000,000,000 bytes per device**. This bounds how much a stale/skewed source can hide in
future Internet usage. LAN totals record only bytes actually excluded, including carry
settled on later ticks. Thus Internet + LAN reconstructs the raw-router running total;
separate raw day/month fields would duplicate this information. `LastRawCounterBytes`
still stores the observed router counter. Carry may settle in a later day/month and is
not billing-grade attribution. Excess beyond the cap is discarded conservatively.

## Verify the Proxmox bridge before configuring NetPilot

Run these steps **on the Proxmox host**, not inside Jellyfin or CT 109. No live counters
have yet been validated. Example server `192.168.1.20`/MAC `02:00:00:00:00:20` and TV
`192.168.1.50` are placeholders: replace them with DHCP reservations and the guest NIC's
actual MAC. The host is `192.168.1.13`, NetPilot CT 109 is `192.168.1.24`.

1. Install Python 3 and nftables through the host's package manager. Copy this repository's
   `deploy/proxmox/netpilot-lan-exporter/` to `/opt/netpilot-lan-exporter/`. Copy
   `config.example.json` to `/etc/netpilot-lan-exporter.json`, set server IP/MAC, bridge
   `vmbr0` and client list, leaving bind at `127.0.0.1` for the initial experiment.
   Use either `"clients": ["192.168.1.50"]` or `"clientCidr": "192.168.1.0/24"`
   (remove `clients`). CIDR expansion is bounded to 1024 addresses and excludes network,
   broadcast and server addresses. Prefer a narrow explicit client list initially.
2. Generate and review a file. Generation never executes nft or changes the firewall:

   ```sh
   python3 /opt/netpilot-lan-exporter/exporter.py --config /etc/netpilot-lan-exporter.json --print-rules > /root/netpilot-lan.nft
   cat /root/netpilot-lan.nft
   nft --check --file /root/netpilot-lan.nft
   ```

3. Explicitly apply the reviewed file:

   ```sh
   nft --file /root/netpilot-lan.nft
   nft list table bridge netpilot_lan
   nft -j list table bridge netpilot_lan
   ```

   The batch creates-if-absent, deletes and recreates **only** `bridge netpilot_lan`.
   Reapplying is idempotent in structure but resets its counters. It never flushes other
   tables. The forward chain contains no drop/reject/mark/NAT actions and has accept policy;
   other firewall chains can still drop traffic. It matches `meta ibrname "vmbr0"`, the
   server's source/destination MAC, and directional IPv4 pairs in a per-element counter
   set. Two rules serve all clients. This uses nftables' supported
   [per-element counters](https://wiki.nftables.org/wiki-nftables/index.php/Sets)
   and [bridge forward hook](https://wiki.nftables.org/wiki-nftables/index.php/Bridge_filtering).
   Require an nft version whose JSON output includes each element's `counter.bytes`;
   older versions omitting them fail exporter health instead of silently returning zero.
4. Record both counters and router usage, play a known-size movie to the Wi-Fi TV for a
   few minutes, then read `nft list table bridge netpilot_lan` again. The server→TV pair
   should grow by roughly the stream bytes, with a smaller reverse pair for acknowledgments.
   Confirm the pair's sum roughly matches the router increment for **each** endpoint.
   Stop streaming: both pair counters should stop except background traffic.
5. Play YouTube on the TV with Jellyfin stopped. The TV router counter should increase;
   Jellyfin pair counters should not. This distinguishes Internet usage from local streaming.
   Test the exporter JSON locally and inspect NetPilot across at least two polling ticks
   after enabling the URL (the first flow reading only sets baselines).

If these comparisons fail, keep NetPilot's URL empty and investigate the bridge path,
firmware units, offload and guest MAC before relying on quotas. To remove only this
experiment: `nft delete table bridge netpilot_lan`.

## Install the read-only exporter

The exporter uses only Python's standard library. It executes exactly
`/usr/sbin/nft -j list table bridge netpilot_lan`, using a subprocess argument list with a
2-second deadline. `/flows` returns the reading; `/health` verifies counters can be read.
Both require `Authorization: Bearer <token>` when `token` is non-empty. No write endpoint
exists. Requests have a 3-second socket deadline, 2 KB path / 8 KB header checks, and GET
bodies are refused. nft JSON is limited to 2 MB after capture. Its HTTP server is serial;
firewall access to one trusted client. It does not log request paths, headers or tokens.

1. Generate a random token, put it in the root-owned config (mode `0600`), and set `bind`
   to the host's **specific LAN IP `192.168.1.13`**, port `9918`. Default is loopback;
   wildcard binding is refused. Keep the token out of source control and command history.
2. In the **existing Proxmox host firewall**, allow TCP 9918 from `192.168.1.24` (CT 109)
   only, then deny other sources to this port. Use its normal management interface and
   preserve existing SSH/management rules; the generated counter table does not implement
   this access policy. Do not port-forward the exporter. Plain HTTP exposes the bearer
   token to the trusted LAN; use an HTTPS reverse proxy if that LAN is not trusted.
3. Install/start the service:

   ```sh
   install -m 0644 /opt/netpilot-lan-exporter/netpilot-lan-exporter.service /etc/systemd/system/
   systemctl daemon-reload
   systemctl enable --now netpilot-lan-exporter
   systemctl status netpilot-lan-exporter
   ```

   The example runs as root with only `CAP_NET_ADMIN` available because nft list needs
   it; the application does not mutate nftables. The unit never applies rules. Arrange
   host boot persistence by including the reviewed `/root/netpilot-lan.nft` in the host's
   existing nftables boot configuration or a separate administrator-owned boot unit.
   Do not replace the Proxmox firewall ruleset. Without restored rules, health returns 503.
4. From CT 109, make authenticated GET requests to `/health` and `/flows` using the token
   header (avoid putting secrets in shared logs). Confirm other LAN clients cannot connect.
   With the token empty the service permits unauthenticated reads: firewall isolation still
   applies. Unsupported HTTP methods have no write behaviour.

The wire contract is source-independent; any exporter returning it can be used:

```json
{"generatedAtUtc":"2026-10-15T12:00:00Z","flows":[{"src":"192.168.1.20","dst":"192.168.1.50","bytes":123456}]}
```

`bytes` is a nonnegative signed-64-bit cumulative directional count, never a delta. IPs
must be canonical strings, each directional pair occurs once, and extra fields are ignored.
`generatedAtUtc` may be omitted/null and is metadata, not a clock for period attribution.
Serve fresh readings: NetPilot records read time but does not reject a stale generated time.

## NetPilot configuration and failures

`HttpLocalTrafficSource` lives in `NetPilot.Data`, the existing infrastructure adapter
project. The source contract and delta logic live in Core. Agent and Web register the
same source/store because manual router refresh also tracks usage. Configure both with:

```json
"NetPilot": {
  "LocalTraffic": { "Url": "http://192.168.1.13:9918/flows", "Token": "your-secret" }
}
```

For Docker, add to the CT's gitignored `deploy/docker/.env`:

```dotenv
NETPILOT_LANTRAFFIC_URL=http://192.168.1.13:9918/flows
NETPILOT_LANTRAFFIC_TOKEN=your-secret
```

Compose maps these to `NetPilot__LocalTraffic__Url` / `__Token` in **both** containers.
Recreate both containers to pick up configuration. Empty URL selects `NullLocalTrafficSource`.
The HTTP deadline is 5 seconds, including response/body reads, with a 2 MB response cap.
The token is sent only as a bearer header and is redacted from HTTP client logging.

Configured sources throw on timeout, non-2xx, malformed/incomplete/invalid/duplicate flows.
Tracking isolates source failures, uses all router bytes that tick, freezes flow baselines
and device carry, and publishes "unavailable since ..." status. Warnings are activity
entries, at most one per hour using an atomic persisted claim across Agent/Web processes
and restarts, without exception text, URL or token. Successful reads clear outage status; no source means "not configured".
Normal caller cancellation still cancels the tracking operation. Database/log persistence
errors are not disguised as source outages.

On recovery the unchanged baseline makes accumulated outage LAN available for subtraction
from **future** router deltas/carry, without rewriting previous periods. This can temporarily
over-count Internet, then under-count it later (and lose excess at the carry cap). The
pre-existing Agent/manual-refresh read-modify-write race also applies to flow baselines
and carry; simultaneous processes can race. Warning claims use a database transaction.
This remains a home network estimate, not a billing meter.

Quotas → Usage exclusions → **LAN-only device** works without an exporter. All future
router deltas for that device become LAN, Internet stays zero, and carry is cleared.
This suits the home-only Jellyfin server. **Remote viewers and every other Internet use
from that device would be hidden too.** Disable the flag before enabling remote streaming.
It does not remove historical bytes or subtract anything from the TV automatically.

## Limits and expected behaviour, unverified live

- Real Proxmox bridge counters, the generated nft syntax on the installed host/kernel,
  service permissions/startup, firewall isolation and reboot restoration are not live-tested.
- Wi-Fi TV traffic must traverse `vmbr0` between the guest NIC and router uplink; a path
  entirely inside another switch/router/bridge is invisible. Proxmox firewall bridges,
  VLANs and guest NIC MAC rewriting may need adapted matches after inspection.
- Hardware/software offload can bypass counters. Byte overhead and snapshot timing may
  differ from the router. The router's per-endpoint counting/units need the experiment above.
- If the router blocks inter-client traffic, the stream cannot pass the bridge; these
  counters cannot observe or repair that connectivity issue.
- Host reboot/reapplying rules resets counters. A numerically lower value is handled;
  reset-and-regrow past the old value between polls is inherently undetectable, as for
  router counters. Traffic before the first flow baseline is never retroactively excluded.
- Provided exporter counts configured IPv4 pairs only, with no LAN host discovery. IPv6,
  multicast and other LAN pairs remain Internet-classified unless another source covers them.
- DHCP address reuse can attribute flows to another device. Reserve server/client addresses.
  When switching to an unrelated exporter/counter universe, stop both apps and back up the
  database; an administrator must rebaseline `lan_flow_state` and clear per-device carry.
- Missing router usage cannot be reconstructed from flow counters; existing skip behaviour
  stays. Period boundaries and polling gaps retain the limitations in
  [usage tracking](phase2-usage-tracking-feature.md).
- Dashboard rendering, a real exporter→Agent/Web exchange and quota decisions against
  live traffic remain unverified. Automated quota coverage uses deterministic readings.
