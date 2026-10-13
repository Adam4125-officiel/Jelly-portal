#!/usr/bin/env python3
"""Tiny Jellyfin admin client for the local dev server (standard library only).

    jf.py wait                       wait until the server answers
    jf.py bootstrap                  finish the first-run wizard, create the dev admin
    jf.py repos URL [URL ...]        replace the plugin repositories
    jf.py packages [NAME]            list the catalog (versions and their target ABI)
    jf.py install NAME [VERSION]     install from the catalog, like the dashboard does
    jf.py plugins                    list installed plugins and their status
    jf.py get PATH                   GET an API path and print the JSON
    jf.py configure [--url U] [--key K] [--admins-only] [--priority NAMES] [--window-hours N]
                                     write the Jelly Portal configuration file (restart Jellyfin after)
    jf.py create-user NAME           create a non-administrator user (password in dev/data/dev-<name>.json)
    jf.py summary [--as NAME]        GET /JellyPortal/Summary as the dev admin or as that user
"""
from __future__ import annotations

import json
import os
import secrets
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

BASE = os.environ.get("JELLYPORTAL_DEV_URL", "http://127.0.0.1:8096")
STATE = Path(__file__).resolve().parent / "data" / "dev-admin.json"
CLIENT = 'MediaBrowser Client="JellyPortalDev", Device="dev-script", DeviceId="jellyportal-dev-script", Version="0.0.1"'


def call(method: str, path: str, body=None, token: str | None = None, timeout: int = 60):
    headers = {"Authorization": CLIENT if not token else CLIENT + f', Token="{token}"'}
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw and response.headers.get_content_type() == "application/json" else raw.decode(errors="replace"))
    except urllib.error.HTTPError as err:
        return err.code, err.read().decode(errors="replace")[:300]


def wait(seconds: int = 180) -> None:
    deadline = time.time() + seconds
    while time.time() < deadline:
        try:
            # /System/Info/Public answers while the server is still starting (the wizard endpoints
            # then return 503 with an HTML holding page): /health only says Healthy once it is up.
            status, body = call("GET", "/health", timeout=5)
            if status == 200 and "Healthy" in str(body):
                return
        except OSError:
            pass
        time.sleep(2)
    sys.exit("Jellyfin did not answer in time")


def login(state: Path | None = None) -> str:
    admin = json.loads((state or STATE).read_text())
    status, body = call("POST", "/Users/AuthenticateByName", {"Username": admin["name"], "Pw": admin["password"]})
    if status != 200:
        sys.exit(f"login failed: {status} {body}")
    return body["AccessToken"]


PLUGIN_CONFIG = Path(__file__).resolve().parent / "data" / "config" / "plugins" / "configurations" / "Jellyfin.Plugin.JellyPortal.xml"
DEFAULT_PORTAL_URL = "http://172.17.0.1:8099"
DEFAULT_PORTAL_KEY = "dev-key-0123456789abcdef0123456789abcdef0123456789ab"


def configure(args: list[str]) -> None:
    """Writes the plugin's XML configuration the way Jellyfin stores it. The key is [JsonIgnore]d, so the
    standard configuration API cannot set it; the file is the dev way (Jellyfin reads it at start)."""
    from xml.sax.saxutils import escape
    opts = {"url": DEFAULT_PORTAL_URL, "key": DEFAULT_PORTAL_KEY, "priority": "", "window-hours": "24",
            "poll": "60", "cache": "30", "admins-only": "false"}
    it = iter(args)
    for a in it:
        if a == "--admins-only":
            opts["admins-only"] = "true"
        elif a.startswith("--") and a[2:] in opts:
            opts[a[2:]] = next(it)
        else:
            sys.exit(f"unknown option {a}")
    PLUGIN_CONFIG.parent.mkdir(parents=True, exist_ok=True)
    PLUGIN_CONFIG.write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n'
        f'  <PortalUrl>{escape(opts["url"])}</PortalUrl>\n  <ApiKey>{escape(opts["key"])}</ApiKey>\n'
        f'  <PollSeconds>{opts["poll"]}</PollSeconds>\n  <CacheSeconds>{opts["cache"]}</CacheSeconds>\n'
        f'  <SoonWindowHours>{opts["window-hours"]}</SoonWindowHours>\n'
        f'  <PriorityServices>{escape(opts["priority"]).replace(chr(44), chr(10))}</PriorityServices>\n'
        f'  <AdminsOnly>{opts["admins-only"]}</AdminsOnly>\n</PluginConfiguration>\n', encoding="utf-8")
    print(f"wrote {PLUGIN_CONFIG.name} (url={opts['url']}, admins-only={opts['admins-only']}, priority={opts['priority']!r})")


def create_user(name: str) -> None:
    token = login()
    password = secrets.token_urlsafe(10)
    status, out = call("POST", "/Users/New", {"Name": name, "Password": password}, token)
    if status >= 300:
        sys.exit(f"create user failed: {status} {out}")
    state = STATE.parent / f"dev-{name}.json"
    state.write_text(json.dumps({"name": name, "password": password}))
    state.chmod(0o600)
    print(f"created user {name} (credentials in {state.name})")


def bootstrap() -> None:
    wait()
    _, info = call("GET", "/System/Info/Public")
    if info.get("StartupWizardCompleted"):
        print("wizard already completed")
        return
    password = secrets.token_urlsafe(12)
    steps = [
        ("POST", "/Startup/Configuration", {"ServerName": "jellyportal-dev", "UICulture": "en-US",
                                            "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"}),
        ("GET", "/Startup/User", None),
        ("POST", "/Startup/User", {"Name": "dev", "Password": password}),
        ("POST", "/Startup/RemoteAccess", {"EnableRemoteAccess": False}),
        ("POST", "/Startup/Complete", None),
    ]
    for method, path, body in steps:
        status, out = call(method, path, body)
        print(f"{method} {path} -> {status}")
        if status >= 300:
            sys.exit(f"wizard step failed: {out}")
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps({"name": "dev", "password": password}))
    STATE.chmod(0o600)
    print(f"dev admin created (credentials in {STATE.relative_to(Path.cwd()) if STATE.is_relative_to(Path.cwd()) else STATE})")


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 1
    cmd, args = argv[0], argv[1:]
    if cmd == "wait":
        wait()
        print("up")
    elif cmd == "bootstrap":
        bootstrap()
    elif cmd == "repos":
        repos = [{"Name": url.rsplit("/", 2)[-2] if "manifest" in url else url, "Url": url, "Enabled": True} for url in args]
        status, out = call("POST", "/Repositories", repos, login())
        print("set repositories ->", status, out if status >= 300 else "")
    elif cmd == "packages":
        status, packages = call("GET", "/Packages", token=login())
        for package in packages:
            if args and args[0].lower() not in package["name"].lower():
                continue
            print(f'{package["name"]}  guid={package["guid"]}')
            for v in package["versions"]:
                print(f'   {v["version"]:>10}  abi={v.get("targetAbi")}  {v.get("sourceUrl")}')
    elif cmd == "install":
        name, version = args[0], (args[1] if len(args) > 1 else None)
        token = login()
        _, packages = call("GET", "/Packages", token=token)
        package = next(p for p in packages if p["name"] == name)
        chosen = next(v for v in package["versions"] if version in (None, v["version"]))
        query = {"assemblyGuid": package["guid"], "version": chosen["version"], "repositoryUrl": chosen["repositoryUrl"]}
        status, out = call("POST", f"/Packages/Installed/{urllib.parse.quote(name)}?{urllib.parse.urlencode(query)}", token=token)
        print(f"install {name} {chosen['version']} (first catalog entry: abi={chosen.get('targetAbi')}) -> {status} {out if status >= 300 else ''}")
    elif cmd == "plugins":
        status, plugins = call("GET", "/Plugins", token=login())
        for p in plugins:
            print(f'{p["Name"]:<22} {p["Version"]:<12} {p["Status"]}')
    elif cmd == "configure":
        configure(args)
    elif cmd == "create-user":
        create_user(args[0])
    elif cmd == "summary":
        who = args[1] if len(args) > 1 and args[0] == "--as" else None
        token = login(STATE.parent / f"dev-{who}.json" if who else None)
        status, out = call("GET", "/JellyPortal/Summary", token=token)
        print(status, json.dumps(out) if not isinstance(out, str) else out)
    elif cmd == "get":
        status, out = call("GET", args[0], token=login())
        print(json.dumps(out, indent=2) if not isinstance(out, str) else out)
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
