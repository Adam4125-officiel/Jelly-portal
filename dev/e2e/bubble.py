"""End to end: the header icon and its bubble, in a real browser, for every state, Modern and Legacy layouts,
desktop and phone widths. Writes screenshots to ./screenshots/<layout>-<size>/ and prints PASS/FAIL per check.

Needs: dev/jellyfin.sh up, dev/dev.sh, the fake portal on 172.17.0.1:8099, a poll of 10 s and a cache of 5 s
(`dev/jf.py configure --cache 5 --poll 10`). Usage: bubble.py [scenario ...]   (default: all)
"""
import json
import sys
import time
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import BASE, LANGUAGE, SHOTS, Recorder, chrome, login, set_language  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

FAKE = "http://172.17.0.1:8099"
CACHE_WAIT = 5.7   # the cache lifetime is 5 s

# scenario -> expected icon state
EXPECTED = {
    "ok": "ok", "slow": "ok", "down": "down", "degraded": "degraded", "incident_only": "degraded",
    "maintenance_ongoing": "maintenance_ongoing", "manual_maintenance": "maintenance_ongoing",
    "maintenance_soon": "maintenance_soon", "maintenance_far": "ok", "muted_window": "ok",
    "announcements": "ok", "busy": "down",
    "disabled": "unknown", "bad_key": "unknown", "outdated": "unknown", "bad_gateway": "unknown",
}
# fragments the open bubble must contain for a scenario, in the language of the run (JP_LANG, English by default)
TEXTS = {
    "en": {
        "ok": ["All services are operational", "Jellyfin", "Seerr", "Nextcloud"],   # nothing competes: the whole list is shown, not folded
        "down": ["At least one service is down", "Nextcloud", "Down", "Ongoing incidents", "Nextcloud is unreachable", "\u00b7 for "],
        "degraded": ["Degraded performance", "Sonarr", "Degraded"],
        "incident_only": ["Degraded performance", "Ongoing incidents", "Intermittent network errors", "Monitoring"],
        "maintenance_ongoing": ["Maintenance in progress", "NAS disk replacement", "ends in", "Read-only while the array rebuilds."],
        "manual_maintenance": ["Maintenance in progress", "Seerr", "Under maintenance"],
        "maintenance_soon": ["Maintenance planned soon", "Upcoming maintenance", "Jellyfin update", "in 2 h", "today at"],
        "maintenance_far": ["Upcoming maintenance", "Router firmware", "in 2 d"],
        "announcements": ["Announcements", "Movie night", "Pinned", "Heads up", "It worked!"],
        "slow": ["Radarr", "Slow"],
        "busy": ["Ongoing incidents", "Maintenance in progress", "Upcoming maintenance", "Announcements", "NAS disk replacement"],
        "disabled": ["Status unavailable", "switched off"],
        "bad_key": ["Status unavailable", "refuses the configured API key"],
        "outdated": ["Status unavailable", "1.12.0"],
        "bad_gateway": ["Status unavailable", "The status portal cannot be reached"],
    },
    "fr": {
        "ok": ["Tous les services fonctionnent", "Jellyfin", "Seerr", "Nextcloud"],
        "down": ["Au moins un service est hors service", "Nextcloud", "Hors service", "Incidents en cours", "Nextcloud is unreachable", "depuis"],
        "degraded": ["Fonctionnement d\u00e9grad\u00e9", "Sonarr", "D\u00e9grad\u00e9"],
        "incident_only": ["Fonctionnement d\u00e9grad\u00e9", "Incidents en cours", "Intermittent network errors", "Sous surveillance"],
        "maintenance_ongoing": ["Maintenance en cours", "NAS disk replacement", "se termine dans", "Read-only while the array rebuilds."],
        "manual_maintenance": ["Maintenance en cours", "Seerr", "En maintenance"],
        "maintenance_soon": ["Maintenance pr\u00e9vue prochainement", "Maintenances \u00e0 venir", "Jellyfin update", "dans 2 h", "aujourd\u2019hui \u00e0"],
        "maintenance_far": ["Maintenances \u00e0 venir", "Router firmware", "dans 2 j"],
        "announcements": ["Annonces", "Movie night", "\u00c9pingl\u00e9e", "Heads up", "It worked!"],
        "slow": ["Radarr", "Lent"],
        "busy": ["Incidents en cours", "Maintenance en cours", "Maintenances \u00e0 venir", "Annonces", "NAS disk replacement"],
        "disabled": ["\u00c9tat indisponible", "d\u00e9sactiv\u00e9e"],
        "bad_key": ["\u00c9tat indisponible", "refuse la cl\u00e9 API"],
        "outdated": ["\u00c9tat indisponible", "1.12.0"],
        "bad_gateway": ["\u00c9tat indisponible", "Impossible de joindre le status portal"],
    },
}
TEXT = TEXTS[LANGUAGE]
UNTIL = {"en": "until ", "fr": "jusqu\u2019\u00e0 "}[LANGUAGE]
SAME_DAY = {"en": "until today", "fr": "jusqu\u2019\u00e0 aujourd\u2019hui"}[LANGUAGE]
HEADINGS = {"en": ["Services", "Ongoing incidents"], "fr": ["Services", "Incidents en cours"]}[LANGUAGE]
COMBOS = [  # (name, layout in localStorage, viewport)
    ("modern-desktop", "modern", {"width": 1280, "height": 800}),
    ("legacy-desktop", "desktop-legacy", {"width": 1280, "height": 800}),
    ("modern-phone", "modern", {"width": 390, "height": 844}),
    ("legacy-phone", "mobile-legacy", {"width": 390, "height": 844}),
]
failures: list[str] = []
passes = 0


def check(ok: bool, what: str) -> None:
    global passes
    if ok:
        passes += 1
    else:
        failures.append(what)
        print("FAIL " + what)


def scenario(name: str) -> None:
    urllib.request.urlopen(urllib.request.Request(f"{FAKE}/__scenario/{name}", method="POST")).read()


RECT = "(sel) => { const e = document.querySelector(sel); if (!e) return null; const r = e.getBoundingClientRect(); return {left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height}; }"
COLOR = "(sel) => getComputedStyle(document.querySelector(sel)).color"


def rgb(css: str):
    nums = [float(x) for x in css[css.index("(") + 1:css.index(")")].replace("/", ",").replace(" ", ",").split(",") if x]
    return (nums + [1.0])[:4]


def colour_class(css: str) -> str:
    r, g, b, a = rgb(css)
    if a < 0.8:
        return "grey"
    if r > 170 and g < 130 and b < 130:
        return "red"
    if r > 230 and 130 < g < 200 and b < 90:
        return "orange"
    if b > 230 and r > 140 and g < 170 and b - g > 60:
        return "violet"
    return "white" if min(r, g, b) > 200 else "other"


COLOUR = {"down": "red", "degraded": "orange", "maintenance_ongoing": "violet", "unknown": "grey"}


def other_icon(layout_name: str) -> str:
    return ".MuiAppBar-root .MuiIconButton-root[aria-label]:not([data-jellyportal-icon]) >> nth=0" if layout_name.startswith("modern") else ".skinHeader .headerSearchButton"


def run_case(page, rec, combo, name, vw, vh, sc, folder):
    expected = EXPECTED[sc]
    page.reload(wait_until="domcontentloaded")
    page.wait_for_selector("[data-jellyportal-icon]", state="attached", timeout=25000)
    # Whenever it is caught, an icon that has no state yet must be invisible: no grey flash before the first answer.
    flash = page.evaluate("() => { const i = document.querySelector('[data-jellyportal-icon]'); return i.getAttribute('data-state') === 'loading' ? getComputedStyle(i).visibility : 'n/a'; }")
    check(flash in ("hidden", "n/a"), f"[{combo} {sc}] no grey flash while the first answer is awaited (visibility={flash})")
    page.wait_for_function("(s) => { const i = document.querySelector('[data-jellyportal-icon]'); return i && i.getAttribute('data-state') === s; }", arg=expected, timeout=25000)
    page.wait_for_timeout(400)
    tag = f"[{combo} {sc}]"
    icons = page.locator("[data-jellyportal-icon]").count()
    check(icons == 1, f"{tag} exactly one icon (found {icons})")
    layout_attr = page.get_attribute("[data-jellyportal-icon]", "data-jellyportal-icon")
    check(layout_attr == ("modern" if combo.startswith("modern") else "legacy"), f"{tag} icon built for the {combo.split('-')[0]} header (got {layout_attr})")

    colour = page.evaluate(COLOR, "[data-jellyportal-icon]")
    if expected in COLOUR:
        check(colour_class(colour) == COLOUR[expected], f"{tag} icon colour is {COLOUR[expected]} (computed {colour})")
    else:
        sibling = page.evaluate("(sel) => getComputedStyle(document.querySelector(sel)).color", other_icon(combo).replace(" >> nth=0", ""))
        check(colour == sibling, f"{tag} OK/soon icon has the colour of the other header icons ({colour} vs {sibling})")
    badge = page.evaluate("() => !document.querySelector('.jellyportal-badge').hidden && getComputedStyle(document.querySelector('.jellyportal-badge')).display !== 'none'")
    check(badge == (expected == "maintenance_soon"), f"{tag} clock badge shown only for maintenance_soon (shown={badge})")
    anim = page.evaluate("() => getComputedStyle(document.querySelector('[data-jellyportal-icon] > svg')).animationName")
    check((anim == "jellyportal-pulse") == (expected == "maintenance_ongoing"), f"{tag} pulses only for maintenance in progress (animation={anim})")
    page.screenshot(path=str(folder / f"{sc}-closed.png"), clip={"x": 0, "y": 0, "width": vw, "height": 58 if combo.startswith("modern") else 62})

    # --- open
    url_before = page.url
    page.click("[data-jellyportal-icon]")
    page.wait_for_selector(".jellyportal-bubble", timeout=5000)
    page.wait_for_timeout(250)
    check(page.url == url_before, f"{tag} opening does not change the URL")
    check(page.locator(".jellyportal-bubble").count() == 1, f"{tag} one bubble")
    if expected in COLOUR:
        pressed = page.evaluate(COLOR, "[data-jellyportal-icon]")
        check(colour_class(pressed) == COLOUR[expected], f"{tag} the icon keeps its state colour while pressed and focused (computed {pressed})")
    check(page.get_attribute("[data-jellyportal-icon]", "aria-expanded") == "true", f"{tag} aria-expanded is true while open")
    icon = page.evaluate(RECT, "[data-jellyportal-icon]")
    bub = page.evaluate(RECT, ".jellyportal-bubble")
    arrow = page.evaluate(RECT, ".jellyportal-arrow")
    check(bub["top"] >= icon["bottom"], f"{tag} the bubble is under the icon (bubble top {bub['top']:.0f} >= icon bottom {icon['bottom']:.0f})")
    arrow_x = (arrow["left"] + arrow["right"]) / 2
    check(icon["left"] <= arrow_x <= icon["right"], f"{tag} the arrow points at the icon ({arrow_x:.0f} within {icon['left']:.0f}-{icon['right']:.0f})")
    arrow_y = (arrow["top"] + arrow["bottom"]) / 2
    check(abs(arrow_y - bub["top"]) <= 4, f"{tag} the arrow is centred on the bubble's top edge (arrow centre {arrow_y:.1f}, bubble top {bub['top']:.1f})")
    check(bub["left"] >= 7.5 and bub["right"] <= vw - 7.5, f"{tag} the bubble stays inside the screen ({bub['left']:.0f}..{bub['right']:.0f} of {vw})")
    if vw < 600:
        check(abs(bub["width"] - (vw - 16)) < 1.5 and abs(bub["left"] - 8) < 1.5, f"{tag} phone: a bubble of the screen minus 16 px, not full screen (width {bub['width']:.0f} of {vw})")
    else:
        check(abs(bub["width"] - 360) < 1.5, f"{tag} desktop: about 360 px wide (width {bub['width']:.0f})")
        check(abs(bub["right"] - min(vw - 8, icon["right"] + 12)) < 1.5, f"{tag} right-aligned to the icon (bubble right {bub['right']:.0f}, icon right {icon['right']:.0f})")
    check(bub["height"] <= vh * 0.7 + 1, f"{tag} at most 70% of the screen high ({bub['height']:.0f} of {vh})")
    check(page.evaluate("() => getComputedStyle(document.querySelector('.jellyportal-bubble')).position") == "fixed", f"{tag} the bubble is positioned over the page (fixed)")
    body = page.text_content(".jellyportal-bubble")   # the DOM text: inner_text would apply the headings' uppercase
    for fragment in TEXT.get(sc, []):
        check(fragment in body, f"{tag} the bubble says \"{fragment}\"")
    if sc == "maintenance_soon":
        check(SAME_DAY not in body and UNTIL in body, f"{tag} the end of a same-day maintenance is just a time ({UNTIL.strip()} 04:19, not {SAME_DAY} at 04:19)")
    if sc in ("down", "busy"):
        order = page.evaluate("() => [...document.querySelectorAll('.jellyportal-heading > span:first-child')].map(e => e.textContent)")
        check(order[:2] == HEADINGS, f"{tag} incidents come right after the services that are down ({order})")
    page.screenshot(path=str(folder / f"{sc}-open.png"))

    # --- close three ways, one bubble at a time
    page.mouse.click(vw // 2, vh - 40)
    page.wait_for_timeout(200)
    check(page.locator(".jellyportal-bubble").count() == 0, f"{tag} a click outside closes it")
    page.click("[data-jellyportal-icon]")
    page.wait_for_selector(".jellyportal-bubble")
    page.keyboard.press("Escape")
    page.wait_for_timeout(200)
    check(page.locator(".jellyportal-bubble").count() == 0, f"{tag} Escape closes it")
    check(page.evaluate("() => document.activeElement && document.activeElement.hasAttribute('data-jellyportal-icon')"), f"{tag} Escape gives the focus back to the icon")
    page.click("[data-jellyportal-icon]")
    page.wait_for_selector(".jellyportal-bubble")
    page.click("[data-jellyportal-icon]")
    page.wait_for_timeout(200)
    check(page.locator(".jellyportal-bubble").count() == 0, f"{tag} a click on the icon closes it")
    check(page.url == url_before, f"{tag} the URL never changed through any of it")


wanted = sys.argv[1:] or list(EXPECTED)
set_language()
print(f"language of this run: {LANGUAGE}")
with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=chrome(), headless=True)
    contexts = {}
    for combo, layout, viewport in COMBOS:
        ctx = browser.new_context(viewport=viewport)
        ctx.add_init_script(f"localStorage.setItem('layout', '{layout}')")
        page = ctx.new_page()
        rec = Recorder(page)
        scenario("ok")
        login(page)
        folder = SHOTS / LANGUAGE / combo
        folder.mkdir(parents=True, exist_ok=True)
        contexts[combo] = (page, rec, viewport, folder)
    for sc in wanted:
        scenario(sc)
        time.sleep(CACHE_WAIT)
        for combo, (page, rec, viewport, folder) in contexts.items():
            try:
                run_case(page, rec, combo, combo, viewport["width"], viewport["height"], sc, folder)
            except Exception as error:  # a timeout here is itself a failure of the case
                check(False, f"[{combo} {sc}] the case could not complete: {str(error).splitlines()[0][:160]}")
        print(f"-- {sc}: done")
    for combo, (page, rec, viewport, folder) in contexts.items():
        check(rec.problems_of_ours() == [], f"[{combo}] nothing of ours in the console, no page error, no failed request: {rec.problems_of_ours()[:3]}")
        check(len([m for t, m in rec.plugin_console() if t == "info"]) >= 1, f"[{combo}] the client announced itself")
    browser.close()

print(f"\n{passes} checks passed, {len(failures)} failed")
sys.exit(1 if failures else 0)
