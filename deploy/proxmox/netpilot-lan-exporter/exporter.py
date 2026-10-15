#!/usr/bin/env python3
"""Read-only nftables bridge counters. No packet capture or nft mutations."""
import argparse
from datetime import datetime, timezone
import hmac
from http.server import BaseHTTPRequestHandler, HTTPServer
import ipaddress
import json
from pathlib import Path
import re
import subprocess

TABLE = "netpilot_lan"


def extract_flows(raw):
    """Read per-element directional counters from nft -j list table bridge netpilot_lan."""
    payload = json.loads(raw)
    if not isinstance(payload, dict) or not isinstance(payload.get("nftables"), list):
        raise ValueError("Missing nftables list")
    flows = {}
    for entry in payload["nftables"]:
        if not isinstance(entry, dict):
            raise ValueError("Invalid nft entry")
        counter_set = entry.get("set", {})
        if not isinstance(counter_set, dict):
            raise ValueError("Invalid nft set")
        if counter_set.get("name") != "flows":
            continue
        if counter_set.get("family") != "bridge" or counter_set.get("table") != TABLE:
            raise ValueError("Unexpected counter table")
        for wrapped in counter_set.get("elem", []):
            element = wrapped["elem"]
            pair = element["val"]["concat"]
            if len(pair) != 2:
                raise ValueError("Invalid flow pair")
            src, dst = (str(ipaddress.IPv4Address(peer)) for peer in pair)
            count = element["counter"]["bytes"]
            if type(count) is not int or not 0 <= count <= 2**63 - 1 or (src, dst) in flows:
                raise ValueError("Invalid or duplicate counter")
            flows[src, dst] = {"src": src, "dst": dst, "bytes": count}
    if not flows:
        raise ValueError("No flow counters; install the generated rules first")
    return list(flows.values())


def read_flows():
    completed = subprocess.run(
        ["/usr/sbin/nft", "-j", "list", "table", "bridge", TABLE],
        capture_output=True, check=True, timeout=2, text=True)
    if len(completed.stdout) > 2 * 1024 * 1024:
        raise ValueError("Counter response too large")
    return {"generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "flows": extract_flows(completed.stdout)}


def authorized(header, token):
    return not token or hmac.compare_digest(
        (header or "").encode("utf-8"), ("Bearer " + token).encode("utf-8"))


def generate_rules(config):
    """One counter per directional IP pair; two rules regardless of client count."""
    server = ipaddress.IPv4Address(config["serverIp"])
    mac = config["serverMac"].lower()
    bridge = config.get("bridge", "vmbr0")
    if not re.fullmatch(r"(?:[0-9a-f]{2}:){5}[0-9a-f]{2}", mac):
        raise ValueError("Invalid serverMac")
    if not re.fullmatch(r"[a-zA-Z0-9_.-]{1,15}", bridge):
        raise ValueError("Invalid bridge")
    if bool(config.get("clients")) == bool(config.get("clientCidr")):
        raise ValueError("Supply either clients or clientCidr")
    if config.get("clientCidr"):
        network = ipaddress.IPv4Network(config["clientCidr"], strict=True)
        if network.num_addresses > 1024:
            raise ValueError("CIDR too large (maximum 1024 addresses)")
        clients = list(network.hosts())
    else:
        clients = [ipaddress.IPv4Address(peer) for peer in config["clients"]]
    clients = sorted(set(clients) - {server})
    if not 1 <= len(clients) <= 1024 or not server.is_private or any(not peer.is_private for peer in clients):
        raise ValueError("Use 1..1024 private LAN clients and a private server IP")
    # 'add table' tolerates an existing table; the batch replaces only this owned table.
    elements = ", ".join(f"{src} . {dst}" for client in clients
                         for src, dst in ((server, client), (client, server)))
    lines = [f"add table bridge {TABLE}", f"delete table bridge {TABLE}",
             f"table bridge {TABLE} {{", "  set flows {",
             "    type ipv4_addr . ipv4_addr;", "    counter;",
             f"    elements = {{ {elements} }}", "  }", "  chain forward {",
             "    type filter hook forward priority -200; policy accept;"]
    for direction in ("saddr", "daddr"):
        lines.append(f'    meta ibrname "{bridge}" ether {direction} {mac} '
                     'ip saddr . ip daddr @flows')
    return "\n".join(lines + ["  }", "}", ""])



class Handler(BaseHTTPRequestHandler):
    server_version = "NetPilotLAN/1"

    def setup(self):
        super().setup()
        self.connection.settimeout(3)

    def do_GET(self):
        if len(self.path) > 2048 or sum(len(k) + len(v) for k, v in self.headers.items()) > 8192:
            self.send_payload(431, {"error": "Request too large"})
        elif self.headers.get("Transfer-Encoding") or self.headers.get("Content-Length", "0") != "0":
            self.send_payload(400, {"error": "Request bodies are not accepted"})
        elif not authorized(self.headers.get("Authorization"), self.server.token):
            self.send_payload(401, {"error": "Unauthorized"})
        elif self.path not in ("/flows", "/health"):
            self.send_payload(404, {"error": "Not found"})
        else:
            try:
                reading = read_flows()
            except (OSError, subprocess.SubprocessError, ValueError, TypeError, KeyError):
                self.send_payload(503, {"error": "LAN counters unavailable"})
                return
            self.send_payload(200, reading if self.path == "/flows" else {"status": "ok"})

    def send_payload(self, status, payload):
        encoded = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(encoded)
        self.close_connection = True

    def log_message(self, *_args):
        pass  # Do not log request paths or headers containing credentials.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", default="/etc/netpilot-lan-exporter.json")
    parser.add_argument("--print-rules", action="store_true", help="Print only; never applies rules")
    args = parser.parse_args()
    config = json.loads(Path(args.config).read_text())
    if args.print_rules:
        print(generate_rules(config), end="")
        return
    bind = str(ipaddress.IPv4Address(config.get("bind", "127.0.0.1")))
    if bind == "0.0.0.0":
        raise ValueError("Bind to one specific host IP")
    port = config.get("port", 9918)
    token = config.get("token", "")
    if type(port) is not int or not 1 <= port <= 65535 or not isinstance(token, str):
        raise ValueError("Invalid port or token")
    with HTTPServer((bind, port), Handler) as server:
        server.token = token
        server.serve_forever()


if __name__ == "__main__":
    main()
