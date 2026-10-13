#!/usr/bin/env python3
"""A stand-in for the Status Portal's display-device API, for developing and testing the plugin.

It answers `GET /api/device/summary` exactly as the real portal (1.12.0, `detail=full`) does, for the one
request the plugin makes, and it is deliberately STRICT about that request: the key must be in the
`X-Api-Key` header, and the query must be exactly
    sections=services,incidents,maintenance,announcements&services=all&detail=full
Anything else, `resources`, `vms` or `jellyfin` above all, is answered with a loud 400, because the plugin
shows its data to every Jellyfin user and must never ask for the inside of the server.

The scenario can be changed while it runs:
    curl -X POST http://HOST:PORT/__scenario/maintenance_soon
    curl http://HOST:PORT/__scenarios

Standard library only. Bind it to an address the Jellyfin container can reach and the LAN cannot, for example
the Docker bridge gateway: `dev/fake_portal.py --host 172.17.0.1`.
"""
from __future__ import annotations

import argparse
import json
import sys
import threading
import time
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

DEFAULT_KEY = "dev-key-0123456789abcdef0123456789abcdef0123456789ab"
EXPECTED_QUERY = {"sections": ["services,incidents,maintenance,announcements"], "services": ["all"], "detail": ["full"]}
RANK = {"down": 0, "degraded": 1, "maintenance": 2, "slow": 3, "operational": 4}
COUNT_ORDER = ("operational", "slow", "degraded", "maintenance", "down")

# The portal cuts names at 64 bytes and the service arrays at 16 names in full mode.
NAME_BYTES = 64

BASE_SERVICES = [
    ("Jellyfin", "operational", 42), ("Seerr", "operational", 55), ("Radarr", "operational", 31),
    ("Sonarr", "operational", 36), ("Prowlarr", "operational", 70), ("qBittorrent", "operational", 28),
    ("Nextcloud", "operational", 90), ("NAS", "operational", 12),
]


def iso(moment: datetime) -> str:
    return moment.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def cut(text: str, limit: int = NAME_BYTES) -> str:
    raw = " ".join(str(text).split()).encode("utf-8")
    if len(raw) <= limit:
        return raw.decode("utf-8")
    return raw[: limit - 3].decode("utf-8", errors="ignore").rstrip() + "..."


def window(now, title, *, services, starts_in, lasts, description="", active=None):
    starts = now + starts_in
    ends = starts + lasts
    is_active = (starts <= now < ends) if active is None else active
    return {"title": cut(title, 120), "state": "active" if is_active else "upcoming",
            "services": cut(", ".join(services), 120), "starts": iso(starts), "ends": iso(ends),
            "service_names": [cut(s) for s in services][:16], "description": cut(description, 400)}


def incident(now, title, *, services=(), status="investigating", ago=timedelta(minutes=25)):
    return {"title": cut(title, 120), "status": status, "since": iso(now - ago),
            "services": cut(", ".join(services), 120), "service_names": [cut(s) for s in services][:16]}


def announcement(title, text, kind="info", pinned=False):
    return {"title": cut(title, 100), "text": cut(text.replace("**", ""), 500), "type": kind, "pinned": pinned}


def scenario_parts(name: str, now: datetime):
    """(services, incidents, windows, announcements) for a scenario; services are (name, status, ms)."""
    services = [list(s) for s in BASE_SERVICES]
    incidents: list = []
    windows: list = []
    announcements: list = []

    def set_status(service, status):
        for s in services:
            if s[0] == service:
                s[1] = status
                if status in ("down", "degraded", "maintenance"):
                    s[2] = None

    h, m = timedelta(hours=1), timedelta(minutes=1)
    if name == "ok":
        pass
    elif name == "slow":
        set_status("Radarr", "slow")
    elif name == "down":
        set_status("Nextcloud", "down")
        incidents.append(incident(now, "Nextcloud is unreachable", services=["Nextcloud"], ago=20 * m))
    elif name == "degraded":
        set_status("Sonarr", "degraded")
    elif name == "incident_only":
        incidents.append(incident(now, "Intermittent network errors", status="monitoring", ago=2 * h))
    elif name == "maintenance_ongoing":
        windows.append(window(now, "NAS disk replacement", services=["NAS", "Nextcloud"], starts_in=-30 * m,
                              lasts=h + 15 * m, description="Read-only while the array rebuilds."))
        set_status("NAS", "maintenance")
        set_status("Nextcloud", "maintenance")
    elif name == "manual_maintenance":
        set_status("Seerr", "maintenance")
    elif name == "maintenance_soon":
        windows.append(window(now, "Jellyfin update", services=["Jellyfin", "Seerr"], starts_in=2 * h + 5 * m, lasts=h,
                              description="Short outage while the server restarts."))
    elif name == "maintenance_far":
        windows.append(window(now, "Router firmware", services=["NAS"], starts_in=timedelta(days=3), lasts=h))
    elif name == "muted_window":
        windows.append(window(now, "Maintenance of a muted box", services=["Muted box"], starts_in=-10 * m, lasts=h,
                              active=True))
    elif name == "announcements":
        announcements += [announcement("Movie night", "Friday at 8pm, bring snacks.", "info", True),
                          announcement("Heads up", "The upload limit changes next week.", "warning"),
                          announcement("It worked!", "Backups are green again.", "success")]
    elif name == "busy":
        for s, status in (("Nextcloud", "down"), ("Sonarr", "degraded"), ("Radarr", "slow"), ("NAS", "maintenance")):
            set_status(s, status)
        services += [[f"Extra service {i:02d}", "operational", 20 + i] for i in range(1, 13)]
        incidents += [incident(now, "Nextcloud is unreachable", services=["Nextcloud"], ago=20 * m),
                      incident(now, "Slow downloads", services=["qBittorrent", "Prowlarr"], status="identified",
                               ago=3 * h)]
        windows += [window(now, "NAS disk replacement", services=["NAS"], starts_in=-30 * m, lasts=h),
                    window(now, "Jellyfin update", services=["Jellyfin"], starts_in=2 * h, lasts=h,
                           description="Restart of the server."),
                    window(now, "Router firmware", services=["NAS", "Seerr"], starts_in=timedelta(days=2), lasts=h),
                    window(now, "Seerr upgrade", services=["Seerr"], starts_in=5 * h, lasts=30 * m)]
        announcements += [announcement("Movie night", "Friday at 8pm.", "info", True),
                          announcement("Warning", "Expect interruptions tonight.", "warning"),
                          announcement("Resolved", "Everything is back.", "success")]
    else:
        raise KeyError(name)
    return [tuple(s) for s in services], incidents, windows, announcements


OK_SCENARIOS = ("ok", "slow", "down", "degraded", "incident_only", "maintenance_ongoing", "manual_maintenance",
                "maintenance_soon", "maintenance_far", "muted_window", "announcements", "busy")
FAILURE_SCENARIOS = ("timeout", "disabled", "bad_key", "outdated", "bad_json", "bad_gateway", "slow_answer")
ALL_SCENARIOS = OK_SCENARIOS + FAILURE_SCENARIOS


def build(name: str, now: datetime | None = None, *, full: bool = True) -> dict:
    """The device API answer for a scenario. `full=False` is what a portal older than 1.12.0 would send."""
    now = now or datetime.now(timezone.utc)
    services, incidents, windows, announcements = scenario_parts(name, now)
    ordered = sorted(services, key=lambda s: RANK[s[1]])
    counts = {k: sum(1 for s in services if s[1] == k) for k in COUNT_ORDER}
    items = []
    for svc_name, status, ms in ordered:
        item = {"name": cut(svc_name), "status": status}
        if ms is not None and status in ("operational", "slow"):
            item["ms"] = ms
        items.append(item)
    body = {"v": 1, "now": iso(now), "site": "Dev Portal", "overall": "operational"}
    if full:
        body["detail"] = "full"
    body["services"] = {"total": len(services), **counts, "items": items}
    body["incidents"] = {"open": len(incidents), "items": incidents}
    body["maintenance"] = {"active": sum(1 for w in windows if w["state"] == "active"),
                           "upcoming": sum(1 for w in windows if w["state"] == "upcoming"), "items": windows}
    body["announcements"] = {"count": len(announcements), "items": announcements}
    if not full:  # the screen-sized form: no arrays, no descriptions, strings cut short
        for i in body["incidents"]["items"]:
            i.pop("service_names", None)
            i["services"] = cut(i["services"], 32)
        for w in body["maintenance"]["items"]:
            w.pop("service_names", None)
            w.pop("description", None)
            w["services"] = cut(w["services"], 32)
    return body


def dumps(body: dict) -> bytes:
    return json.dumps(body, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


class Handler(BaseHTTPRequestHandler):
    server_version = "FakePortal"
    scenario = "ok"
    key = DEFAULT_KEY
    lock = threading.Lock()

    def log_message(self, fmt, *args):  # one line per request on stderr
        sys.stderr.write("%s %s\n" % (time.strftime("%H:%M:%S"), fmt % args))

    def _send(self, status, body=b"", content_type="application/json; charset=utf-8"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):  # noqa: N802
        path = urlsplit(self.path).path
        if path.startswith("/__scenario/"):
            name = path.rsplit("/", 1)[1]
            if name not in ALL_SCENARIOS:
                return self._send(404, json.dumps({"error": "unknown scenario", "valid": ALL_SCENARIOS}).encode())
            with Handler.lock:
                Handler.scenario = name
            sys.stderr.write(f"== scenario: {name}\n")
            return self._send(200, json.dumps({"scenario": name}).encode())
        self._send(405, b'{"error":"method not allowed"}')

    def do_GET(self):  # noqa: N802
        parts = urlsplit(self.path)
        if parts.path == "/__scenarios":
            return self._send(200, json.dumps({"current": Handler.scenario, "all": ALL_SCENARIOS}).encode())
        if parts.path != "/api/device/summary":
            return self._send(404, b'{"error":"not found"}')
        scenario = Handler.scenario

        if scenario == "disabled":
            return self._send(404, b'{"error":"not found"}')
        if self.headers.get("X-Api-Key") != Handler.key or scenario == "bad_key":
            return self._send(401, b'{"error":"Unauthorized"}')

        query = parse_qs(parts.query, keep_blank_values=True)
        if query != EXPECTED_QUERY:
            sys.stderr.write(f"!! UNEXPECTED QUERY from the plugin: {parts.query!r}\n")
            return self._send(400, json.dumps({"error": "this stand-in only answers the plugin's exact request",
                                               "expected": "&".join(f"{k}={v[0]}" for k, v in EXPECTED_QUERY.items())}).encode())
        if scenario == "timeout":
            time.sleep(60)
            return self._send(200, dumps(build("ok")))
        if scenario == "slow_answer":
            time.sleep(3)
            return self._send(200, dumps(build("ok")))
        if scenario == "bad_gateway":
            return self._send(502, b"<html>502 Bad Gateway</html>", "text/html")
        if scenario == "bad_json":
            return self._send(200, b"<html>not the portal</html>", "text/html")
        if scenario == "outdated":
            return self._send(200, dumps(build("down", full=False)))
        self._send(200, dumps(build(scenario)))


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8099)
    parser.add_argument("--key", default=DEFAULT_KEY)
    parser.add_argument("--scenario", default="ok", choices=ALL_SCENARIOS)
    args = parser.parse_args(argv)
    Handler.key, Handler.scenario = args.key, args.scenario
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.daemon_threads = True
    sys.stderr.write(f"fake portal on http://{args.host}:{args.port} scenario={args.scenario}\n")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
