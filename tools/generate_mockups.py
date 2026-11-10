#!/usr/bin/env python3
"""Regenerate mockups/ as static snapshots of docs/design/prototype.

Every screen reuses the prototype's own tokens.css + prototype.css and mirrors the
markup the prototype renders (same classes, same copy, same sample data), so the
mock screens and the prototype stay one design.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "mockups"

ICONS = {
    "home": '<path d="m3 10 9-7 9 7v10H14v-6h-4v6H3Z"/>',
    "devices": '<rect x="6" y="2" width="12" height="20" rx="3"/><path d="M10 18h4"/>',
    "usage": '<path d="M4 20V10m8 10V4m8 16v-7"/>',
    "automations": '<path d="m13 2-9 12h7l-1 8 10-13h-7Z"/>',
    "activity": '<path d="M4 5h2m4 0h10M4 12h2m4 0h10M4 19h2m4 0h10"/>',
    "settings": '<path d="m9 3-1 3-3 1 1 4-2 2 2 3 3-1 2 3h3l1-3 3-1 1-3-2-2 1-3-3-1-1-3Z"/><circle cx="12" cy="11" r="3"/>',
    "search": '<circle cx="10" cy="10" r="6"/><path d="m15 15 6 6"/>',
    "moon": '<path d="M20 15A9 9 0 0 1 9 3 9 9 0 1 0 20 15Z"/>',
    "shield": '<path d="m12 2 8 4v6c0 5-8 10-8 10S4 17 4 12V6Z"/><path d="m8 12 3 3 5-6"/>',
    "router": '<rect x="3" y="12" width="18" height="8" rx="2"/><path d="M6 12V4m12 8V4M7 16h1m4 0h1m4 0h1"/>',
    "wifi": '<path d="M2 8a16 16 0 0 1 20 0M5 12a11 11 0 0 1 14 0m-11 4a6 6 0 0 1 8 0"/><circle cx="12" cy="20" r="1"/>',
    "arrow": '<path d="M4 12h16m-6-6 6 6-6 6"/>',
    "back": '<path d="M20 12H4m6-6-6 6 6 6"/>',
    "tv": '<rect x="2" y="4" width="20" height="13" rx="2"/><path d="M8 21h8m-4-4v4"/>',
    "laptop": '<rect x="4" y="3" width="16" height="13" rx="2"/><path d="M2 20h20l-2-4H4Z"/>',
    "server": '<rect x="4" y="3" width="16" height="7" rx="2"/><rect x="4" y="14" width="16" height="7" rx="2"/><path d="M8 6h.1M8 17h.1m5-11h4m-4 11h4"/>',
    "game": '<path d="M7 7h10c3 0 5 11 3 12s-5-4-6-4h-4c-1 0-4 5-6 4S4 7 7 7Z"/><path d="M8 9v5m-2-2h4m6-2h.1m2 3h.1"/>',
    "camera": '<path d="M3 7h12v11H3Zm12 4 6-3v10l-6-3M7 7l2-3h4"/>',
    "pause": '<path d="M8 4v16m8-16v16"/>',
    "add": '<path d="M12 4v16M4 12h16"/>',
    "block": '<circle cx="12" cy="12" r="9"/><path d="m6 6 12 12"/>',
    "bell": '<path d="M5 17h14l-2-4V8a5 5 0 0 0-10 0v5Zm5 3h4"/>',
    "warning": '<path d="m12 3 10 18H2Z"/><path d="M12 9v5m0 3h.1"/>',
    "check": '<path d="m5 12 4 4L20 5"/>',
    "clock": '<circle cx="12" cy="12" r="9"/><path d="M12 6v6l4 2"/>',
    "close": '<path d="m6 6 12 12M6 18 18 6"/>',
    "more": '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
    "download": '<path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/>',
    "lock": '<rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V6a4 4 0 0 1 8 0v4"/>',
    "refresh": '<path d="M20 7v5h-5M4 17v-5h5M5 7a8 8 0 0 1 14-1M5 18a8 8 0 0 0 14-1"/>',
    "tune": '<path d="M3 6h18M3 12h18M3 18h18M7 3v6m10 0v6M9 15v6"/>',
    "cloud": '<path d="M6 18a5 5 0 0 1-1-10 7 7 0 0 1 13-1 5 5 0 0 1 1 11Z"/>',
    "eye": '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>',
    "leaf": '<path d="M20 3C6 2 1 10 6 17s16 0 14-14ZM5 20 16 8"/>',
}


def icon(name):
    return f'<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">{ICONS.get(name, ICONS["devices"])}</svg>'


def tile(name, large=False):
    return f'<span class="symbol-tile{" large" if large else ""}">{icon(name)}</span>'


def chip(label, kind="", name=""):
    return f'<span class="chip {kind}">{icon(name) if name else ""}{label}</span>'


def button(text, action, variant="button", name=""):
    return f'<button class="{variant}" data-action="{action}">{icon(name) if name else ""}{text}</button>'


def link(text, href, variant="text-button", name=""):
    return f'<a class="{variant}" href="#{href}">{icon(name) if name else ""}{text}</a>'


def head(eyebrow, title, copy, actions=""):
    inner = f'<div class="actions">{actions}</div>' if actions else ""
    return f'<div class="page-head"><div><div class="eyebrow">{eyebrow}</div><h1>{title}</h1><p>{copy}</p></div>{inner}</div>'


def section(title, body, cls="", extra=""):
    return f'<section class="card {cls}"><div class="section-head"><h2>{title}</h2>{extra}</div>{body}</section>'


def ring(percent, label="of daily budget"):
    return (f'<div class="ring" style="--ring-fill:{percent}" role="img" aria-label="{percent}% {label}">'
            '<svg viewBox="0 0 120 120" aria-hidden="true"><circle class="track" cx="60" cy="60" r="49" pathLength="100"/>'
            '<circle class="value" cx="60" cy="60" r="49" pathLength="100"/></svg>'
            f'<div class="ring-label">{percent}%<small>{label}</small></div></div>')


DEVICES = [
    dict(id="01", name="Pixel phone", cat="Mobile", type="devices", used=1.4, lan=0.1, online=True, medium="WiFi · 5 GHz", ip="192.168.1.31", protected=True),
    dict(id="02", name="Samsung TV", cat="Television", type="tv", used=4.2, lan=12, online=True, medium="WiFi · 5 GHz", ip="192.168.1.50"),
    dict(id="03", name="Jellyfin server", cat="Computer", type="server", used=0, lan=11.8, online=True, medium="Ethernet", ip="192.168.1.20", protected=True, lanOnly=True),
    dict(id="04", name="PS5", cat="Game console", type="game", used=0.6, lan=0, online=False, medium="WiFi · 5 GHz", ip="192.168.1.61"),
    dict(id="05", name="IP camera", cat="IP camera", type="camera", used=0.1, lan=0.1, online=True, medium="WiFi · 2.4 GHz", ip="192.168.1.71"),
    dict(id="06", name="Laptop", cat="Laptop", type="laptop", used=2.1, lan=0, online=True, medium="WiFi · 5 GHz", ip="192.168.1.41"),
]


def mac(device):
    return f"02-00-00-00-00-{device['id']}"


def restriction(device):
    if device["id"] == "02":
        return chip("512 Kbps · manual", "throttled")
    if device["id"] == "04":
        return chip("Blocked · recorded", "blocked")
    if device.get("protected"):
        return chip("Protected", "ok", "shield")
    return chip("No restriction")


def consumers():
    ordered = sorted(DEVICES, key=lambda d: -d["used"])[:4]
    rows = []
    for device in ordered:
        rows.append(
            f'<div class="consumer">{tile(device["type"])}<div>'
            f'<a href="#/devices/{mac(device)}"><strong>{device["name"]}</strong></a>'
            f'<div class="meter"><span style="--fill:{device["used"] / 4.2 * 100}%"></span></div></div>'
            f'<div class="number">{device["used"]:.1f} <small>GB</small></div></div>')
    return "".join(rows)


def chart(period="week", device=False):
    labels = {"month": ["1–3", "4–6", "7–9", "10–12", "13–15", "16"],
              "day": ["Today"],
              "week": ["Sat 10", "Sun 11", "Mon 12", "Tue 13", "Wed 14", "Thu 15", "Fri 16"]}[period]
    vals = {"month": [35, 44, 39, 41, 46.6, 8.4], "day": [8.4],
            "week": [13.2, 16.5, 18.4, 15.8, 17.1, 22.2, 8.4]}[period]
    lans = {"month": [30, 42, 38, 28, 34, 24], "day": [24],
            "week": [16, 10, 18, 14, 21, 19, 24]}[period]
    if device and period == "week":
        vals = [3.2, 3.8, 5.4, 4.1, 4.8, 6.2, 4.2]
        lans = [8, 7, 9, 10, 8, 10, 12]
    maximum = max(v + l for v, l in zip(vals, lans))
    columns = "".join(
        f'<div class="chart-column"><div class="bar-pair"><div class="bar" style="--height:{v / maximum * 140}px"></div>'
        f'<div class="bar lan" style="--height:{l / maximum * 140}px"></div></div><small>{labels[i]}</small></div>'
        for i, (v, l) in enumerate(zip(vals, lans)))
    rows = "".join(
        f"<tr><td>{labels[i]}</td><td>{v}</td><td>{l}</td></tr>" for i, (v, l) in enumerate(zip(vals, lans)))
    return (f'<div class="usage-chart" role="img" aria-label="Internet and LAN usage, {period}. Exact values in the data table below.">'
            f"{columns}</div>"
            '<div class="legend"><span><i class="swatch"></i>Internet</span><span><i class="swatch lan"></i>LAN excluded · both endpoints</span></div>'
            f"<details><summary>View chart data</summary><table><caption>Usage · decimal GB · sample data</caption>"
            f"<thead><tr><th>Period</th><th>Internet GB</th><th>LAN GB</th></tr></thead><tbody>{rows}</tbody></table></details>")


NAV = [("home", "Home", "/"), ("devices", "Devices", "/devices"), ("usage", "Usage", "/usage"),
       ("automations", "Automations", "/automations"), ("activity", "Activity", "/activity")]


def shell(active, title, body):
    nav = "".join(
        f'<a class="nav-item{" active" if key == active else ""}" href="#{path}"'
        f'{" aria-current=\"page\"" if key == active else ""}>{icon(key)}<span>{label}</span></a>'
        for key, label, path in NAV)
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>NetPilot · {title} · Mock</title>
<link rel="stylesheet" href="../docs/design/tokens.css">
<link rel="stylesheet" href="../docs/design/prototype/prototype.css">
</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<aside class="navigation" aria-label="Primary navigation">
  <a class="brand" href="#/"><span class="brand-mark">n<span>p</span></span><span>NetPilot<small>HOME NETWORK</small></span></a>
  <nav>{nav}</nav>
  <div class="nav-utility">
    <a href="#/settings">Settings</a>
    <a href="#/settings/safety">Safety Center</a>
    <div class="network-label"><span class="status-dot"></span> Archer AX53<small>Single household · Cairo</small></div>
  </div>
</aside>
<div class="workspace">
  <header class="app-bar">
    <span class="app-context">Home network <span class="muted">/ Cairo</span></span>
    <div class="app-tools">
      <button class="icon-button" aria-label="Search devices and pages">{icon("search")}</button>
      <button class="icon-button" aria-label="Change color theme">{icon("moon")}</button>
      <a class="settings-link" href="#/settings" aria-label="Settings">{icon("settings")}</a>
    </div>
  </header>
  <a href="#/settings/safety" class="mode-strip">
    <span class="mode-main">{icon("shield")}<span>Quotas: Dry-run</span><span aria-hidden="true">·</span><span>Rules: Forced dry-run</span></span>
    <small>Safety Center {icon("arrow")}</small>
  </a>
  <main id="main" tabindex="-1">
{body}
  </main>
</div>
</body>
</html>
"""


def home_body():
    attention = (
        f'<div class="notice">{icon("warning")}<div><h3>TV is at 84% of its daily quota</h3>'
        '<p>Dry-run: it would slow down after 5 GB.</p><a href="#/devices/02-00-00-00-00-02">Review Samsung TV '
        f'{icon("arrow")}</a></div></div>'
        f'<div class="notice">{icon("devices")}<div><h3>New device: IP camera</h3>'
        '<p>First seen in this sample household today. Review its category and protection.</p>'
        f'<a href="#/devices/02-00-00-00-00-05">Review device {icon("arrow")}</a></div></div>'
        f'<div class="notice">{icon("leaf")}<div><h3>Verify your LAN traffic source</h3>'
        "<p>A successful read doesn’t prove local streams are counted correctly.</p>"
        f'<a href="#/settings/traffic">Check usage confidence {icon("arrow")}</a></div></div>')
    restrictions = (
        '<div class="list-row"><div class="grow"><h3>Samsung TV</h3><p>512 Kbps · manual speed override</p>'
        f'{chip("Manual limit", "throttled")}</div>{button("Review", "why", "text-button")}</div>'
        '<div class="list-row"><div class="grow"><h3>PS5</h3><p>Manual block · NetPilot record</p>'
        f'{chip("Not router-verified", "blocked")}</div>{button("Unblock", "unblock", "text-button")}</div>'
        "<p class=\"hint\">Automatic quotas and rules are observing. Manual actions operate separately.</p>")
    quick = "".join(
        f'<button class="quick-action" data-action="{action}">{icon(name)}<strong>{title}</strong><small>{hint}</small></button>'
        for name, title, hint, action in [
            ("pause", "Pause internet", "For a little while", "pause"),
            ("add", "Add bonus data", "Choose device & period", "bonus"),
            ("block", "Block a device", "Direct router command", "block"),
            ("wifi", "Guest WiFi · On", "Review before changing", "guest")])
    return (
        head("Friday, 16 October", "A little clarity for your network.", "Your household at a glance. All times in Cairo.",
             button("Pause internet", "pause", "button", "pause")) +
        '<div class="household"><div class="summary">' + tile("router") +
        '<div><h3>Archer AX53 ' + chip("Last read 21:39", "ok") +
        "</h3><p>5 devices online · Agent completed a tick 3 min ago</p></div></div>" +
        link("Network health", "/settings/health", "text-button", "arrow") + "</div>" +
        '<div class="grid">'
        '<section class="card hero span-8 tablet-full"><div class="section-head"><span class="eyebrow">Internet today</span>' +
        chip("Sample household budget") + '</div><div class="hero-body"><div><div class="big-number">8.4 <span>GB</span></div>'
        '<p style="margin-block-start:12px">of a 15 GB daily budget</p><small>6.6 GB left to enjoy today</small></div>' +
        ring(56) + '</div><div class="meter"><span style="--fill:56%"></span></div>'
        '<div class="row between" style="margin-block-start:16px"><small>Month · 214 of 300 GB</small>' +
        link("Explore usage", "/usage", "text-button", "arrow") + "</div></section>" +
        section("Needs your attention", attention, "span-4") +
        section("Top Internet consumers today", consumers(), "span-8", link("All devices", "/devices")) +
        section("Restrictions & previews", restrictions, "span-4") +
        section("Make a quick adjustment",
                f'<div class="quick-grid">{quick}</div>'
                "<p class=\"hint\">Pause and general bonus-data controls are proposed backend additions. All actions here are local simulations.</p>",
                "span-12") +
        "</div>")


def devices_body():
    rows = ""
    for device in DEVICES:
        rows += (
            f'<tr><td><label class="select-box"><input type="checkbox" data-device="{device["id"]}" '
            f'aria-label="Select {device["name"]}"></label></td>'
            f'<td><div class="device-identity">{tile(device["type"])}<div>'
            f'<a href="#/devices/{mac(device)}">{device["name"]}</a>'
            f'<small>{device["cat"]} · <bdi>{device["ip"]}</bdi></small></div></div></td>'
            f'<td>{chip("Online" if device["online"] else "Offline", "ok" if device["online"] else "")}'
            f'<small>{device["medium"]}</small></td>'
            f'<td><strong>{device["used"]:.1f} GB</strong><small>'
            f'{f"{device["lan"]} GB LAN excluded" if device["lan"] else "Internet only"}</small></td>'
            f'<td>{restriction(device)}</td></tr>')
    cards = ""
    for device in DEVICES:
        cards += (
            f'<article class="card device-card"><div class="identity">{tile(device["type"])}'
            f'<div style="flex:1"><a href="#/devices/{mac(device)}">{device["name"]}</a>'
            f'<p class="hint" style="margin:4px 0 0">{device["cat"]} · '
            f'{"Online now" if device["online"] else "Last seen 19:12"}</p></div>'
            f'<label class="select-box"><input type="checkbox" data-device="{device["id"]}" '
            f'aria-label="Select {device["name"]}"></label></div>'
            f'<div class="stats"><span><strong>{device["used"]:.1f} GB</strong><small> Internet today</small></span>'
            f'{restriction(device)}</div><small><bdi>{device["ip"]}</bdi> · {device["medium"]}</small></article>')
    return (
        head("Your network", "Devices", "6 known devices · 5 online now", button("Refresh from router", "refresh-router", "outline", "refresh")) +
        '<div class="toolbar">'
        '<label class="field search-field">Find a device<input id="device-search" type="search" placeholder="Name, MAC or IP address"></label>'
        '<label class="field">Category<select id="category-filter"><option value="all">All categories</option></select></label>'
        '<label class="field">Connection<select id="online-filter"><option value="all">All devices</option>'
        '<option value="online">Online only</option><option value="offline">Offline only</option></select></label>'
        '<label class="field">Group by<select id="group-filter"><option value="category">Category</option>'
        '<option value="none">None</option><option disabled>Person · needs backend</option></select></label></div>'
        '<div id="device-results">'
        '<div class="card table-scroll" style="padding:8px 16px"><table class="device-table">'
        '<caption class="screen-reader-only">Known devices, grouped by category</caption>'
        "<thead><tr><th>Select</th><th>Device / category</th><th>Connection</th><th>Internet today</th><th>Restriction</th></tr></thead>"
        f"<tbody>{rows}</tbody></table></div>"
        f'<div class="device-cards">{cards}</div>'
        "</div>")


def detail_body():
    device = DEVICES[1]
    usage = section("Internet usage",
                    '<div class="stat-grid"><div><small>Today</small><div class="metric">4.2 GB</div></div>'
                    '<div><small>This month</small><div class="metric">89.2 GB</div></div>'
                    '<div><small>LAN today</small><div class="metric">12 GB</div></div></div>' +
                    chart("week", True) +
                    "<p class=\"hint\">Sample Samsung TV daily history; today’s Internet and LAN totals match this device. No hourly samples are available today.</p>",
                    "span-8 tablet-full")
    quota = section("Daily quota",
                    '<div class="row">' + ring(84, "of 5 GB quota") +
                    '<div><strong>4.2 of 5 GB</strong><p class="hint">Dry-run · observing</p></div></div>'
                    '<div class="policy-trail"><span>Samsung TV → Television</span>'
                    '<span>Inherits category → 5 GB/day</span><span>Monthly: no cap → Throttle 512 Kbps</span></div>' +
                    button("Edit quota", "quota", "text-button", "tune"),
                    "span-4")
    restricted = section("Why is this device restricted?",
                        '<p class="hint" style="margin-block-start:0">Claims are persisted intent. Block wins; otherwise the lowest Kbps cap wins. Router observations may differ.</p>'
                        '<div class="detail-claim"><div class="row between"><h3>Manual speed override · 512 Kbps</h3></div>'
                        "<p>This limit is separate from quota/rule dry-run. Base category policy is 8 Mbps.</p>"
                        f'{button("Remove manual override", "remove-override", "text-button")}</div>'
                        '<div class="detail-claim"><h3>Dry-run preview</h3>'
                        "<p>Television quota would throttle at 5 GB. No automatic claim was created.</p>"
                        f'{link("Review firing history", "/activity")}</div>',
                        "span-8")
    settings = section("Device settings",
                       '<label class="check-row"><input id="protect-tv" type="checkbox"><span>Protected device'
                       "<small>Skip automatic throttle and block. Wireless changes can still affect it.</small></span></label>"
                       '<label class="check-row"><input id="lan-only" type="checkbox"><span>LAN-only device'
                       "<small>All future traffic counts as LAN, including real Internet use.</small></span></label>" +
                       button("Block device", "block", "danger", "block") +
                       "<p class=\"hint\">Manual block is a direct router operation. The current product’s protected guard applies to automatic actions.</p>",
                       "span-4")
    rules = section("Rules & bedtime",
                    '<div class="list-row">' + tile("clock") +
                    '<div class="grow"><h3>Bedtime throttle</h3><p>22:00–07:00 · 256 Kbps · all days · Cairo</p>' +
                    chip("Forced dry-run", "dry") + "</div>" + button("Edit schedule", "rule", "text-button") + "</div>"
                    "<p class=\"hint\">A device schedule is a TimeWindow rule. Person calendars and bonus-time requests need new backend work.</p>",
                    "span-6")
    history = section("Recent history",
                      "<p>21:39 · Last router observation saved.</p>"
                      '<p class="hint">20:51 · Came online<br>18:30 · Category speed policy saved<br>Yesterday · Dry-run quota warning at 80%</p>' +
                      link("View all activity", "/activity"),
                      "span-6")
    return (
        f'<div class="breadcrumbs">{link("Devices", "/devices", "text-button", "back")}<span>/ {device["name"]}</span></div>'
        '<div class="identity-header">' + tile(device["type"], True) +
        f'<div><div class="eyebrow">{device["cat"]}</div><h1>{device["name"]}</h1>'
        f'<p class="muted"><bdi class="code">{mac(device)}</bdi> · {device["ip"]}</p></div>'
        '<div class="actions">' + button("Pause", "pause", "button", "pause") +
        button("Edit identity", "identity", "outline", "tune") + "</div></div>"
        '<div class="pills" style="margin-block-end:24px">' +
        chip("Online · read 21:39", "ok") + chip(device["medium"]) + restriction(device) + "</div>"
        f'<div class="grid">{usage}{quota}{restricted}{settings}{rules}{history}</div>')


def usage_body():
    stats = section("Internet & local traffic",
                    '<div class="stat-grid"><div><small>Internet this week</small><div class="metric">111.6 GB</div></div>'
                    '<div><small>LAN excluded</small><div class="metric">122 GB</div></div>'
                    '<div><small>Devices online</small><div class="metric">5 <small>of 6</small></div></div></div>' +
                    chart("week") +
                    "<p class=\"hint\">1 GB = 1,000,000,000 bytes. Historical periods may contain unclassified LAN traffic. Day view shows totals; hourly detail is not available.</p>",
                    "span-8 tablet-full")
    forecast = section("A look ahead",
                       '<div class="preview-label">Proposed forecast · example</div><small>300 GB sample ISP plan · cycle starts 1 Oct</small>'
                       '<div class="metric">Around 21 Oct</div><p>At 17.2 GB/day, the remaining 86 GB would last about five days.</p>'
                       "<p class=\"hint\">Uses seven complete valid days. A real forecast stays unavailable while source accuracy is unresolved.</p>" +
                       button("Edit ISP budget", "budget", "text-button", "tune"),
                       "span-4 forecast")
    talkers = section("Top talkers today", consumers(), "span-6")
    categories = section("By category · today",
                         '<div class="consumer">' + tile("tv") + '<div><strong>Television</strong>'
                         '<div class="meter"><span style="--fill:100%"></span></div></div><b>4.2 GB</b></div>'
                         '<div class="consumer">' + tile("laptop") + '<div><strong>Laptop</strong>'
                         '<div class="meter"><span style="--fill:50%"></span></div></div><b>2.1 GB</b></div>'
                         '<div class="consumer">' + tile("devices") + '<div><strong>Mobile</strong>'
                         '<div class="meter"><span style="--fill:33%"></span></div></div><b>1.4 GB</b></div>'
                         "<p class=\"hint\">Other categories: 0.7 GB Internet. LAN-only server: 0 GB Internet.</p>",
                         "span-6")
    return (
        head("Understand your data", "Usage", "Internet usage, with local traffic separated.",
             button("Export period CSV", "export", "outline", "download")) +
        '<div class="toolbar"><div class="segmented" aria-label="Usage period">'
        '<button data-period="day">Day</button><button data-period="week" class="active" aria-pressed="true">Week</button>'
        '<button data-period="month">Month</button></div><span class="muted">10–16 October 2026 · Africa/Cairo</span></div>'
        f'<div class="banner info">{icon("leaf")}<p>LAN source responds, but counters are not live-verified. Keep enforcement in dry-run. LAN totals count both endpoints.</p></div>'
        f'<div class="grid">{stats}{forecast}{talkers}{categories}</div>')


def automations_body():
    hubs = ""
    for title, ic, tagline, description, route, action in [
        ("Quotas", "usage", "Daily & monthly limits", "Default, category and device allowances.", "quotas", "Edit quotas"),
        ("Rules", "automations", "When this, do that", "Five triggers, four actions, clear explanations.", "rules", "Explore rules"),
        ("Schedules", "clock", "A quieter bedtime", "TimeWindow rules, with a safe return in the morning.", "schedules", "Set bedtime"),
        ("Speed policies", "tune", "A fair share of bandwidth", "Separate download and upload limits by category.", "policies", "Manage policies"),
    ]:
        hubs += (f'<article class="card automation-card span-6">{tile(ic)}<div class="eyebrow">{title}</div>'
                 f'<h2>{tagline}</h2><p class="muted">{description}</p>'
                 f'<a class="outline" href="#/automations/{route}">{icon("arrow")} {action}</a></article>')
    return (
        head("Keep it fair", "Automations", "Simple defaults. Clear causes. You stay in control.") +
        '<nav class="section-tabs" aria-label="Automations sections">'
        '<a class="active" href="#/automations">Overview</a><a class="text-button" href="#/automations/quotas">Quotas</a>'
        '<a class="text-button" href="#/automations/rules">Rules</a><a class="text-button" href="#/automations/schedules">Schedules</a>'
        '<a class="text-button" href="#/automations/policies">Speed policies</a></nav>'
        f'<div class="grid">{hubs}</div>')


def activity_body():
    events = [
        ("21:39", "router", "Agent tick completed", "5 online devices observed. Rules ran in forced dry-run.", "System", ""),
        ("21:36", "usage", "Samsung TV reached 80% of its quota", "4.2 of 5 GB Internet today. Dry-run: would throttle at 5 GB.", "Dry-run", "dry"),
        ("21:12", "automations", "Binge alert evaluated", "Television category · below 10 GB/day. No action taken.", "Rule", ""),
        ("20:51", "devices", "Pixel phone came online", "192.168.1.31 · WiFi 5 GHz · protected device.", "Device", "ok"),
        ("19:12", "bell", "Notification accepted by push service", "Does not prove phone display. This is a sample delivery attempt.", "Transport log", ""),
    ]
    items = ""
    for time, ic, title, body, label, kind in events:
        target = {"quota": ("/devices/02-00-00-00-00-02", "Why / view device"), "rule": ("/automations/rules", "View source"),
                  "device": ("/devices", "View source"), "system": ("/settings/health", "View source"),
                  "notification": ("/settings/health", "View source")}[{"router": "system", "usage": "quota", "automations": "rule",
                                                                     "devices": "device", "bell": "notification"}[ic]]
        items += (f'<li><time>{time}</time>{tile(ic)}<div><div class="row between"><h3>{title}</h3>{chip(label, kind)}</div>'
                  f'<p>{body}</p>{link(target[1], target[0])}</div></li>')
    return (
        head("Know what happened", "Activity", "A clear timeline of your network. Times in Africa/Cairo.") +
        '<nav class="section-tabs" aria-label="Activity views">'
        '<a class="active" href="#/activity">Timeline</a><a class="text-button" href="#/activity/inbox">Inbox · 2</a></nav>'
        '<div class="toolbar"><label class="field">Show events<select id="activity-filter">'
        '<option value="all">Everything</option><option value="quota">Quotas</option><option value="rule">Rules</option>'
        '<option value="device">Devices</option><option value="system">System</option>'
        '<option value="notification">Notifications</option></select></label>'
        '<span class="muted">Friday, 16 October 2026</span></div>'
        f'<div class="card"><ol class="timeline">{items}</ol></div>')


def settings_body():
    items = [
        ("Router", "router", "/settings/router"), ("Wireless", "wifi", "/settings/wireless"),
        ("Notifications", "bell", "/settings/notifications"), ("Safety Center", "shield", "/settings/safety"),
        ("Security", "lock", "/settings/security"), ("LAN source", "leaf", "/settings/traffic"),
        ("HTTPS / Install", "devices", "/settings/app"), ("System health", "activity", "/settings/health"),
        ("Backup", "cloud", "/settings/backup"), ("About / appearance", "tune", "/settings/about"),
    ]
    nav = "".join(f'<a class="text-button" href="#{href}">{title}</a>' for title, _, href in items)
    rows = "".join(
        f'<a class="list-row" style="text-decoration:none;color:inherit" href="#{href}">{tile(ic)}'
        f'<span class="grow"><strong>{title}</strong></span>{icon("arrow")}</a>' for title, ic, href in items)
    return (
        head("Your preferences", "Settings", "A dependable home network starts with clear controls.") +
        f'<div class="settings-layout"><nav class="settings-nav" aria-label="Settings sections">{nav}</nav>'
        '<div><div class="grid">' +
        section("Your household controls",
                "<p>Connect and maintain your network, keep alerts useful, and make safety visible.</p>" + rows,
                "span-12") +
        "</div></div></div>")


PAGES = {
    "home.html": ("home", "Home", home_body),
    "devices.html": ("devices", "Devices", devices_body),
    "device-detail.html": ("devices", "Device detail", detail_body),
    "usage.html": ("usage", "Usage", usage_body),
    "automations-quotas.html": ("automations", "Automations", automations_body),
    "activity.html": ("activity", "Activity", activity_body),
    "settings.html": ("settings", "Settings", settings_body),
}


def main():
    OUT.mkdir(exist_ok=True)
    for name, (active, title, builder) in PAGES.items():
        (OUT / name).write_text(shell(active, title, builder()), encoding="utf-8")
        print("wrote", name)


if __name__ == "__main__":
    main()
