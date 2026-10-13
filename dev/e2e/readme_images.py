"""Regenerates the pictures the README shows (docs/images/), from the real client against the fake portal:
a strip of the six icon states, and the open bubble in the Modern layout, the Legacy layout and on a phone.
Needs the dev Jellyfin, the fake portal and `dev/jf.py configure --cache 5 --poll 10`.
"""
import sys
import time
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import ROOT, Recorder, chrome, login  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

OUT = ROOT / "docs" / "images"
FAKE = "http://172.17.0.1:8099"
STATES = [  # (scenario, caption)
    ("ok", "Tout va bien"), ("maintenance_soon", "Maintenance prévue"), ("degraded", "Dégradé"),
    ("maintenance_ongoing", "Maintenance en cours"), ("down", "Hors service"), ("bad_key", "Portal injoignable"),
]


def scenario(name):
    urllib.request.urlopen(urllib.request.Request(f"{FAKE}/__scenario/{name}", method="POST")).read()
    time.sleep(5.7)


def page_for(browser, layout, viewport, scale=1):
    ctx = browser.new_context(viewport=viewport, device_scale_factor=scale)
    ctx.add_init_script(f"localStorage.setItem('layout', '{layout}')")
    page = ctx.new_page()
    login(page)
    page.wait_for_function("() => { const i = document.querySelector('[data-jellyportal-icon]'); return i && i.getAttribute('data-state') !== 'loading'; }", timeout=25000)
    page.wait_for_timeout(500)   # the colour transition
    page.add_style_tag(content=".jellyportal-btn > svg{animation:none!important}")   # a still frame, at full strength
    return ctx, page


with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=chrome(), headless=True)

    # ---- the six states, side by side
    tiles = []
    for name, caption in STATES:
        scenario(name)
        ctx, page = page_for(browser, "modern", {"width": 1280, "height": 400}, 3)
        box = page.evaluate("() => { const r = document.querySelector('[data-jellyportal-icon]').getBoundingClientRect(); const a = document.querySelector('.MuiAppBar-root .MuiAvatar-root').getBoundingClientRect(); return {x: r.left - 6, w: a.right - r.left + 12}; }")
        shot = OUT / f"_tile-{name}.png"
        page.screenshot(path=str(shot), clip={"x": box["x"], "y": 0, "width": box["w"], "height": 48})
        tiles.append((caption, shot))
        ctx.close()
    cells = "".join(f'<div class="t"><img src="{shot.as_uri()}"><span>{caption}</span></div>' for caption, shot in tiles)
    html = f"""<html><body style="margin:0;background:#101010;font:14px 'Noto Sans',sans-serif;color:#ddd"><style>
      .row{{display:grid;grid-template-columns:repeat(3,auto);gap:22px 34px;padding:22px 26px;width:max-content}}
      .t{{display:flex;flex-direction:column;align-items:center;gap:8px}} img{{height:48px}}</style><div class="row">{cells}</div></body></html>"""
    sheet = OUT / "_states.html"
    sheet.write_text(html)
    page = browser.new_page(viewport={"width": 900, "height": 120}, device_scale_factor=1)
    page.goto(sheet.as_uri())
    page.wait_for_timeout(400)
    page.screenshot(path=str(OUT / "states.png"), full_page=True)
    page.close()
    sheet.unlink()
    for _, shot in tiles:
        shot.unlink()

    # ---- the open bubble, busiest scenario
    scenario("busy")
    for layout, viewport, name, clip in (
        ("modern", {"width": 1280, "height": 760}, "bubble-modern", {"x": 640, "y": 0, "width": 640, "height": 640}),
        ("desktop-legacy", {"width": 1280, "height": 760}, "bubble-legacy", {"x": 640, "y": 0, "width": 640, "height": 640}),
        ("modern", {"width": 390, "height": 760}, "bubble-phone", None),
    ):
        ctx, page = page_for(browser, layout, viewport, 1)
        page.click("[data-jellyportal-icon]")
        page.wait_for_selector(".jellyportal-bubble")
        page.wait_for_timeout(500)
        page.screenshot(path=str(OUT / f"{name}.png"), **({"clip": clip} if clip else {}))
        ctx.close()
    scenario("ok")
    browser.close()

for f in sorted(OUT.glob("*.png")):
    print(f"{f.name}: {f.stat().st_size // 1024} KB")
