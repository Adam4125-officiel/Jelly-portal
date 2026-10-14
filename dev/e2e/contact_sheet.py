"""Builds contact sheets (one PNG of many screenshots) from ./screenshots/<combo>/ so a whole run can be reviewed
at a glance. Reads files only; touches neither Jellyfin nor the portal.
    contact_sheet.py closed <combo> [scenario ...]       the closed headers, right half
    contact_sheet.py open   <combo> <scenario> [...]     the open bubbles side by side
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import LANGUAGE, SHOTS, chrome  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

ORDER = ["ok", "slow", "down", "degraded", "incident_only", "maintenance_ongoing", "manual_maintenance", "maintenance_soon",
         "maintenance_far", "muted_window", "announcements", "busy", "disabled", "bad_key", "outdated", "bad_gateway"]


def build(kind, combo, names):
    phone = "phone" in combo
    width = 390 if phone else 1280
    rows = []
    if kind == "closed":
        crop_w = width if phone else 560
        for n in names:
            src = (SHOTS / LANGUAGE / combo / f"{n}-closed.png").as_uri()
            rows.append(f'<div class="cell"><div class="label">{n}</div><div class="box" style="width:{crop_w}px;height:62px"><img src="{src}" style="margin-left:-{width - crop_w}px"></div></div>')
        css = ".grid{display:grid;grid-template-columns:repeat(%d,auto);gap:6px 18px;padding:10px}" % (1 if phone else 2)
    else:
        for n in names:
            src = (SHOTS / LANGUAGE / combo / f"{n}-open.png").as_uri()
            crop_w = width if phone else 640
            h = 844 if phone else 700
            rows.append(f'<div class="cell"><div class="label">{n}</div><div class="box" style="width:{crop_w}px;height:{h}px"><img src="{src}" style="margin-left:-{width - crop_w}px"></div></div>')
        css = ".grid{display:flex;gap:14px;padding:10px;align-items:flex-start}"
    html = f"""<html><body style="margin:0;background:#262626;font:12px sans-serif;color:#ddd"><style>{css}
    .label{{margin:2px 0;color:#9ad}} .box{{overflow:hidden;border:1px solid #444;background:#101010}} .box img{{display:block}}</style>
    <div class="grid">{''.join(rows)}</div></body></html>"""
    page_file = SHOTS / f"_sheet-{LANGUAGE}-{kind}-{combo}.html"
    page_file.write_text(html)
    return page_file


kind, combo = sys.argv[1], sys.argv[2]
names = sys.argv[3:] or ORDER
html = build(kind, combo, names)
with sync_playwright() as p:
    b = p.chromium.launch(executable_path=chrome(), headless=True, args=["--allow-file-access-from-files"])
    page = b.new_page(viewport={"width": 1400 if kind == "closed" else 1700, "height": 800})
    page.goto(html.as_uri())
    page.wait_for_timeout(500)
    out = SHOTS / f"sheet-{LANGUAGE}-{kind}-{combo}.png"
    page.screenshot(path=str(out), full_page=True)
    print(out)
    b.close()
html.unlink()
