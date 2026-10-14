"""End to end: how the icon and the bubble behave over time and around the rest of the web client.

Survives navigation and header re-renders, pauses polling in a hidden tab and refreshes on return, follows the
configured interval, is invisible and quiet for a user who may not see it and before sign-in, shows times in the
browser's own time zone, recovers from a portal outage by itself, and gives way to Jellyfin's own header menus.

Needs the dev Jellyfin, the fake portal, and `dev/jf.py configure --cache 5 --poll 10`. Runs a few minutes.
"""
import json
import sys
import time
import urllib.request
from datetime import datetime, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import jf  # noqa: E402
from common import BASE, SHOTS, Recorder, chrome, login  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

FAKE = "http://172.17.0.1:8099"
ICON = "[data-jellyportal-icon]"
failures: list[str] = []
passes = 0


def check(ok: bool, what: str) -> None:
    global passes
    if ok:
        passes += 1
        print("PASS " + what)
    else:
        failures.append(what)
        print("FAIL " + what)


def scenario(name: str) -> None:
    urllib.request.urlopen(urllib.request.Request(f"{FAKE}/__scenario/{name}", method="POST")).read()


def config(**changes) -> None:
    token = jf.login()
    status, body = jf.call("POST", "/JellyPortal/Config", changes, token)
    assert status == 200, (status, body)


def new_page(browser, layout="modern", viewport=None, timezone=None):
    ctx = browser.new_context(viewport=viewport or {"width": 1280, "height": 800}, timezone_id=timezone)
    ctx.add_init_script(f"localStorage.setItem('layout', '{layout}')")
    page = ctx.new_page()
    rec = Recorder(page)
    requests: list[float] = []
    page.on("request", lambda r: requests.append(time.time()) if "/JellyPortal/Summary" in r.url else None)
    return ctx, page, rec, requests


def wait_icon(page, state=None, timeout=25000):
    cond = "(s) => { const i = document.querySelector('[data-jellyportal-icon]'); return i && (!s || i.getAttribute('data-state') === s); }"
    page.wait_for_function(cond, arg=state, timeout=timeout)


def icons(page) -> int:
    return page.locator(ICON).count()


def set_hidden(page, hidden: bool) -> None:
    page.evaluate("""(h) => {
        if (!window.__setHidden) {
            Object.defineProperty(document, 'hidden', { configurable: true, get: () => window.__hidden === true });
            Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => (window.__hidden ? 'hidden' : 'visible') });
            window.__setHidden = (v) => { window.__hidden = v; document.dispatchEvent(new Event('visibilitychange')); };
        }
        window.__setHidden(h);
    }""", hidden)


config(adminsOnly=False, pollSeconds=10, cacheSeconds=5)
scenario("ok")
time.sleep(6)

with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=chrome(), headless=True)

    # ---------------------------------------------------------------- not signed in
    ctx, page, rec, reqs = new_page(browser)
    page.goto(f"{BASE}/web/", wait_until="networkidle")
    page.wait_for_timeout(2500)
    check(icons(page) == 0, "before sign-in: no icon")
    check(len(reqs) == 0, f"before sign-in: the summary is never asked for ({len(reqs)} requests)")
    check(rec.problems_of_ours() == [], f"before sign-in: nothing of ours in the console {rec.problems_of_ours()}")
    ctx.close()

    # ---------------------------------------------------------------- navigation and re-renders, both layouts
    for layout, label in (("modern", "Modern"), ("desktop-legacy", "Legacy")):
        ctx, page, rec, reqs = new_page(browser, layout)
        login(page)
        wait_icon(page)
        first = page.locator(ICON).first
        sibling_ok = page.evaluate("() => { const n = document.querySelector('[data-jellyportal-icon]').nextElementSibling; return !!n && (n.classList.contains('MuiIconButton-root') || n.classList.contains('headerSyncButton') || n.classList.contains('headerButton')); }")
        check(sibling_ok, f"[{label}] the icon sits with the other header icons, before them")
        for route in ("#/search", "#/mypreferencesmenu", "#/home", "#/search", "#/home"):
            page.evaluate("(h) => { location.hash = h; }", route)
            page.wait_for_timeout(900)
            check(icons(page) == 1, f"[{label}] still exactly one icon after navigating to {route} (found {icons(page)})")
        page.go_back()
        page.wait_for_timeout(700)
        check(icons(page) == 1, f"[{label}] one icon after Back")
        page.go_forward()
        page.wait_for_timeout(700)
        check(icons(page) == 1, f"[{label}] one icon after Forward")

        page.click(ICON)
        page.wait_for_selector(".jellyportal-bubble")
        page.evaluate("() => { location.hash = '#/search'; }")
        page.wait_for_timeout(700)
        check(page.locator(".jellyportal-bubble").count() == 0 and icons(page) == 1, f"[{label}] navigating closes the bubble and keeps the icon")

        page.evaluate("() => document.querySelector('[data-jellyportal-icon]').remove()")
        page.wait_for_timeout(900)
        check(icons(page) == 1, f"[{label}] an icon wiped by a header re-render comes back by itself")
        page.set_viewport_size({"width": 700, "height": 800})
        page.wait_for_timeout(700)
        page.set_viewport_size({"width": 1280, "height": 800})
        page.wait_for_timeout(700)
        check(icons(page) == 1, f"[{label}] one icon after the window was resized")

        page.evaluate("() => { location.hash = '#/dashboard'; }")
        page.wait_for_timeout(2500)
        dash = icons(page)
        print(f"INFO [{label}] icons on the dashboard page: {dash}")
        check(dash <= 1, f"[{label}] never more than one icon on the dashboard")
        check(rec.problems_of_ours() == [], f"[{label}] nothing of ours in the console through all of that {rec.problems_of_ours()[:3]}")
        ctx.close()

    # ---------------------------------------------------------------- polling: interval, hidden tab, return
    ctx, page, rec, reqs = new_page(browser)
    login(page)
    wait_icon(page)
    page.wait_for_timeout(24000)
    n = len(reqs)
    check(n >= 3, f"polling follows the configured 10 s interval ({n} requests in about 30 s)")
    set_hidden(page, True)
    page.wait_for_timeout(500)
    before = len(reqs)
    page.wait_for_timeout(24000)
    check(len(reqs) == before, f"a hidden tab does not ask the server ({len(reqs) - before} requests in 24 s)")
    set_hidden(page, False)
    page.wait_for_timeout(2500)
    check(len(reqs) > before, "coming back to the tab refreshes at once")
    ctx.close()

    # ---------------------------------------------------------------- the interval follows the setting
    config(pollSeconds=20)
    time.sleep(6)
    ctx, page, rec, reqs = new_page(browser)
    login(page)
    wait_icon(page)
    page.wait_for_timeout(52000)
    gaps = [round(b - a) for a, b in zip(reqs, reqs[1:])]
    check(len(gaps) >= 2 and all(18 <= g <= 23 for g in gaps[-2:]), f"a new polling interval of 20 s is followed (gaps between requests: {gaps})")
    ctx.close()
    config(pollSeconds=10)

    # ---------------------------------------------------------------- failure, then recovery without a reload
    scenario("bad_key")
    time.sleep(6)
    ctx, page, rec, reqs = new_page(browser)
    login(page)
    wait_icon(page, "unknown")
    page.click(ICON)
    page.wait_for_selector(".jellyportal-bubble")
    check("refuse la clé API" in page.text_content(".jellyportal-bubble"), "an outage says why, in words")
    page.keyboard.press("Escape")
    scenario("down")
    t0 = time.time()
    wait_icon(page, "down", timeout=40000)
    check(True, f"the icon recovers by itself after an outage, without a reload ({time.time() - t0:.0f} s)")
    check(rec.problems_of_ours() == [], f"nothing of ours in the console through an outage {rec.problems_of_ours()[:3]}")
    ctx.close()
    scenario("ok")
    time.sleep(6)

    # ---------------------------------------------------------------- a user who may not see it
    config(adminsOnly=True)
    time.sleep(6)
    jf.create_user("viewer") if not (Path(__file__).resolve().parents[1] / "data" / "dev-viewer.json").exists() else None
    ctx, page, rec, reqs = new_page(browser)
    login(page, "viewer")
    page.wait_for_timeout(5000)
    check(icons(page) == 0, "administrators only: an ordinary user sees no icon")
    page.wait_for_timeout(22000)
    check(len(reqs) <= 2, f"administrators only: an ordinary user's browser stops asking after the refusal ({len(reqs)} requests in 27 s)")
    check(rec.problems_of_ours() == [], f"administrators only: and logs nothing of ours {rec.problems_of_ours()[:3]}")
    ctx.close()
    ctx, page, rec, reqs = new_page(browser)
    login(page)
    wait_icon(page)
    check(icons(page) == 1, "administrators only: an administrator still sees the icon")
    ctx.close()
    config(adminsOnly=False)
    time.sleep(6)
    ctx, page, rec, reqs = new_page(browser)
    login(page, "viewer")
    wait_icon(page)
    check(icons(page) == 1, "everyone: the ordinary user sees the icon again")
    ctx.close()

    # ---------------------------------------------------------------- times in the viewer's own time zone
    scenario("maintenance_soon")
    time.sleep(6)
    token = jf.login()
    _, summary = jf.call("GET", "/JellyPortal/Summary", token=token)
    start = summary["maintenance"]["upcoming"][0]["startsAt"]
    for zone in ("Europe/Paris", "America/New_York", "Asia/Tokyo"):
        ctx, page, rec, reqs = new_page(browser, timezone=zone)
        login(page)
        wait_icon(page, "maintenance_soon")
        page.click(ICON)
        page.wait_for_selector(".jellyportal-bubble")
        text = page.text_content(".jellyportal-bubble")
        # The fake portal fixes a start "two hours from now" at every request, so it drifts by a minute between the
        # reading made here and the one the page made: accept the minute before and after.
        wanted = []
        for drift in (0, 60, -60):
            local = (datetime.fromisoformat(start.replace("Z", "+00:00")) + timedelta(seconds=drift)).astimezone(ZoneInfo(zone))
            now_local = datetime.now(ZoneInfo(zone))
            day = "aujourd’hui" if local.date() == now_local.date() else "demain" if (local.date() - now_local.date()).days == 1 else None
            wanted.append(f"{day} à {local:%H:%M}" if day else f"{local:%H:%M}")
        check(any(w in text for w in wanted), f"time zone {zone}: the maintenance reads one of {wanted[0]!r} (±1 min)")
        ctx.close()
    scenario("ok")
    time.sleep(6)

    # ---------------------------------------------------------------- the bubble's link is the public address, never the one the portal is asked with
    scenario("ok")
    for public, label in (("https://status.dev.example", "a public address is set"), ("", "no public address")):
        config(publicUrl=public)
        ctx, page, rec, reqs = new_page(browser)
        login(page)
        wait_icon(page, "ok")
        page.click(ICON)
        page.wait_for_selector(".jellyportal-bubble")
        link = page.locator(".jellyportal-link")
        if public:
            check(link.count() == 1 and link.get_attribute("href") == public + "/", f"[{label}] the bubble's link is that address")
        else:
            check(link.count() == 0, f"[{label}] the bubble has no link at all")
        summaries = [body for url, body in rec.bodies if "/JellyPortal/Summary" in url]
        check(bool(summaries) and all("172.17.0.1" not in body and ":8099" not in body for body in summaries),
              f"[{label}] the address the portal is asked with is in no summary the browser received")
        check(bool(summaries) and all(json.loads(body)["portal"]["url"] == (public or None) for body in summaries),
              f"[{label}] the summary carries exactly the public address ({public or 'null'})")
        ctx.close()
    config(publicUrl="https://status.dev.example")

    # ---------------------------------------------------------------- giving way to Jellyfin's own header menus
    scenario("busy")
    time.sleep(6)
    for layout, label, user_button in (("modern", "Modern", ".MuiAppBar-root .MuiAvatar-root"), ("desktop-legacy", "Legacy", ".skinHeader .headerUserButton")):
        ctx, page, rec, reqs = new_page(browser, layout)
        login(page)
        wait_icon(page, "down")
        page.click(ICON)
        page.wait_for_selector(".jellyportal-bubble")
        page.click(user_button)
        page.wait_for_timeout(700)
        check(page.locator(".jellyportal-bubble").count() == 0, f"[{label}] opening the user menu closes the bubble (never both)")
        page.screenshot(path=str(SHOTS / f"coexist-{label.lower()}-menu.png"))
        page.keyboard.press("Escape")
        page.wait_for_timeout(500)
        page.click(ICON)
        page.wait_for_timeout(500)
        check(page.locator(".jellyportal-bubble").count() <= 1, f"[{label}] never two bubbles")
        page.keyboard.press("Escape")
        # the folded healthy services
        page.click(ICON)
        page.wait_for_selector(".jellyportal-bubble")
        toggle = page.locator(".jellyportal-fold")
        check(toggle.count() == 1, f"[{label}] more than six healthy services are folded behind one line")
        rows_before = page.locator(".jellyportal-service").count()
        toggle.click()
        page.wait_for_timeout(300)
        check(page.locator(".jellyportal-service").count() > rows_before, f"[{label}] the line unfolds them")
        check(page.evaluate("() => { const s = document.querySelector('.jellyportal-scroll'); return s.scrollHeight > s.clientHeight; }"), f"[{label}] a long bubble scrolls inside itself")
        page.mouse.click(10, 790)
        check(rec.problems_of_ours() == [], f"[{label}] nothing of ours in the console {rec.problems_of_ours()[:3]}")
        ctx.close()
    scenario("ok")
    browser.close()

print(f"\n{passes} checks passed, {len(failures)} failed")
sys.exit(1 if failures else 0)
