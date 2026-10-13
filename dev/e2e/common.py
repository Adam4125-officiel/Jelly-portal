"""Shared pieces of the end-to-end checks: drive a real Chromium against the dev Jellyfin.

These are run by hand (they need the Docker Jellyfin from dev/jellyfin.sh and the fake portal), not in CI.
    python3 -m venv dev/data/venv && dev/data/venv/bin/pip install playwright
    dev/data/venv/bin/python dev/e2e/<script>.py
"""
from __future__ import annotations

import glob
import json
import os
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BASE = os.environ.get("JELLYPORTAL_DEV_URL", "http://127.0.0.1:8096")
SHOTS = ROOT / "screenshots"
PORTAL_KEY = "dev-key-0123456789abcdef0123456789abcdef0123456789ab"


def chrome() -> str:
    """A Chromium to launch: JELLYPORTAL_CHROME, or the one Playwright cached, whatever its build number."""
    if os.environ.get("JELLYPORTAL_CHROME"):
        return os.environ["JELLYPORTAL_CHROME"]
    found = sorted(glob.glob(str(Path.home() / ".cache/ms-playwright/chromium-*/chrome-linux*/chrome")))
    if not found:
        raise SystemExit("No Chromium found. Run: dev/data/venv/bin/python -m playwright install chromium")
    return found[-1]


def credentials(name: str = "admin") -> dict:
    path = ROOT / "dev" / "data" / ("dev-admin.json" if name == "admin" else f"dev-{name}.json")
    return json.loads(path.read_text())


class Recorder:
    """Everything the page says that could be a problem, so nothing passes unnoticed."""

    def __init__(self, page):
        self.console: list[tuple[str, str]] = []
        self.errors: list[str] = []
        self.failed: list[str] = []
        self.bodies: list[tuple[str, str]] = []
        page.on("console", lambda m: self.console.append((m.type, m.text)))
        page.on("pageerror", lambda e: self.errors.append(str(e)))
        page.on("requestfailed", lambda r: self.failed.append(f"{r.url} {r.failure}"))
        page.on("response", self._body)

    def _body(self, response):
        try:
            if any(t in (response.headers.get("content-type") or "") for t in ("json", "html", "javascript", "text")):
                self.bodies.append((response.url, response.text()))
        except Exception:
            pass

    def plugin_console(self) -> list[tuple[str, str]]:
        return [(t, m) for t, m in self.console if "[JellyPortal]" in m]

    def problems_of_ours(self) -> list[str]:
        """Errors and warnings that are about this plugin: its own messages, or anything failing under /JellyPortal/."""
        bad = [f"{t}: {m}" for t, m in self.plugin_console() if t in ("error", "warning")]
        bad += [e for e in self.errors if "JellyPortal" in e or "jellyportal" in e]
        bad += [f for f in self.failed if "/JellyPortal/" in f]
        return bad

    def leaked(self, secret: str) -> list[str]:
        return [url for url, body in self.bodies if secret in body]


def login(page, who: str = "admin") -> None:
    """Signs in through the real login form."""
    who_credentials = credentials(who)
    page.goto(f"{BASE}/web/", wait_until="domcontentloaded")
    page.wait_for_selector("#txtManualName", timeout=30000)
    page.fill("#txtManualName", who_credentials["name"])
    page.fill("#txtManualPassword", who_credentials["password"])
    page.keyboard.press("Enter")
    page.wait_for_function("() => !location.hash.includes('login')", timeout=30000)
    page.wait_for_load_state("networkidle")


def strip_ws(text: str) -> str:
    return re.sub(r"\s+", " ", text or "").strip()
