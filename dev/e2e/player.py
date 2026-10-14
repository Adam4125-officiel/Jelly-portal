"""End to end: the icon and the bubble on the playback page, where jellyfin-web draws a header of its own.

In the Modern layout that header is a separate toolbar with no user menu; in the Legacy layout it is the ordinary header
with the class `osdHeader`, in which jellyfin-web hides every button but back, cast and SyncPlay. Both used to lose the
icon during playback. This drives a real video (a short VP9 clip the script makes itself, in a throw-away library of
the dev Jellyfin) and checks, for Modern and Legacy at desktop and phone width:

- the icon is there, visible, in the header and left of the other buttons, in the state the portal reports, and it
  fades with the player's controls and comes back with them;
- the bubble opens over the picture without pausing, navigating or changing the URL, and the video keeps playing;
- Escape closes the bubble and nothing else (it does not hide the player's controls the way jellyfin-web's own Escape
  does), with the controls shown *and* with them faded out, and the video keeps playing;
- a click on the picture that closes the bubble does not also pause the video, while a click on a control still works
  (and a plain click on the picture, with no bubble, does pause: the baseline that proves the check can fail);
- leaving the player puts the icon back in the ordinary header, once, however many times that is repeated.

Needs the dev Jellyfin, the fake portal, and `dev/jf.py configure --cache 5 --poll 10`. Runs a few minutes.
"""
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import jf  # noqa: E402
from common import BASE, ROOT, SHOTS, Recorder, chrome, login, set_language  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

FAKE = "http://172.17.0.1:8099"
ICON = "[data-jellyportal-icon]"
BUBBLE = ".jellyportal-bubble"
MEDIA = ROOT / "dev" / "data" / "media" / "Test"
CLIP = MEDIA / "Test-video.webm"
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


# ---------------------------------------------------------------------------------------------------- the video
def ensure_video() -> str:
    """Makes the clip and the library if they are missing, and returns the clip's item id."""
    MEDIA.mkdir(parents=True, exist_ok=True)
    if not CLIP.exists():
        subprocess.run(["docker", "exec", "jellyportal-dev", "/usr/lib/jellyfin-ffmpeg/ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
                        "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=24:duration=120", "-f", "lavfi", "-i", "sine=frequency=440:duration=120",
                        "-c:v", "libvpx-vp9", "-b:v", "300k", "-deadline", "realtime", "-cpu-used", "8", "-c:a", "libopus", "-b:a", "48k",
                        "/media/Test/Test-video.webm"], check=True)
    token = jf.login()
    _, folders = jf.call("GET", "/Library/VirtualFolders", None, token)
    if not any(f.get("Name") == "Test" for f in folders):
        status, _ = jf.call("POST", "/Library/VirtualFolders?name=Test&collectionType=homevideos&refreshLibrary=true",
                            {"LibraryOptions": {"PathInfos": [{"Path": "/media/Test"}]}}, token)
        assert status == 204, status
    for _ in range(40):
        _, found = jf.call("GET", "/Items?Recursive=true&IncludeItemTypes=Video", None, token)
        if found.get("Items"):
            return found["Items"][0]["Id"]
        time.sleep(2)
    raise SystemExit("The test clip was never indexed by the dev Jellyfin.")


# ---------------------------------------------------------------------------------------------------- helpers
def new_page(browser, layout, viewport):
    ctx = browser.new_context(viewport=viewport)
    ctx.add_init_script(f"localStorage.setItem('layout', '{layout}')")
    page = ctx.new_page()
    return ctx, page, Recorder(page)


def wake(page, vw, vh):
    """Moves the pointer far enough (jellyfin-web ignores less than 10 px) to show the player's controls."""
    page.mouse.move(vw // 2, vh // 2 - 40)
    page.mouse.move(vw // 2 + 60, vh // 2 - 10)
    page.wait_for_timeout(350)


def start_playback(page, item, vw, vh):
    page.goto(f"{BASE}/web/#/details?id={item}", wait_until="domcontentloaded")
    button = page.locator("button.btnPlay:not(.hide):visible").first
    button.wait_for(timeout=30000)
    button.click()
    page.wait_for_function("() => { const v = document.querySelector('video'); return !!v && v.currentTime > 0.5 && !v.paused; }", timeout=40000)
    wake(page, vw, vh)


def video_state(page):
    return page.evaluate("() => { const v = document.querySelector('video'); return v ? { paused: v.paused, t: v.currentTime } : null; }")


def on_player(page) -> bool:
    return page.evaluate("() => location.hash.startsWith('#/video')") and video_state(page) is not None


def icon_info(page):
    """Where the icon is, and whether anything of it can actually be seen (own display/visibility and every ancestor's opacity)."""
    return page.evaluate("""() => {
        const i = document.querySelector('[data-jellyportal-icon]');
        if (!i) { return null; }
        const r = i.getBoundingClientRect(); const cs = getComputedStyle(i);
        let opacity = 1; for (let n = i; n; n = n.parentElement) { opacity *= parseFloat(getComputedStyle(n).opacity); }
        const others = [...i.parentElement.children].filter(c => c !== i && c.getBoundingClientRect().width > 0);
        return { count: document.querySelectorAll('[data-jellyportal-icon]').length, state: i.getAttribute('data-state'),
                 shown: cs.display !== 'none' && cs.visibility === 'visible' && opacity > 0.5 && r.width > 0,
                 x: r.x, y: r.y, w: r.width, h: r.height, right: r.right, bottom: r.bottom,
                 leftOfOthers: others.every(c => c.getBoundingClientRect().x >= r.right - 1), siblings: others.length,
                 inPlayerHeader: !!i.closest('.osdHeader') };
    }""")


def wait_icon_shown(page, shown=True, timeout=12000):
    page.wait_for_function("(s) => { const i = document.querySelector('[data-jellyportal-icon]'); if (!i) { return false; }"
                           " let o = 1; for (let n = i; n; n = n.parentElement) { o *= parseFloat(getComputedStyle(n).opacity); }"
                           " const v = getComputedStyle(i).visibility === 'visible' && i.getClientRects().length > 0 && o > 0.5;"
                           " return v === s; }", arg=shown, timeout=timeout)


def bubble_open(page) -> bool:
    return page.locator(BUBBLE).count() == 1


def run_combo(browser, item, name, layout, vw, vh):
    print(f"\n=== {name} ({layout}, {vw}x{vh}) ===")
    ctx, page, rec = new_page(browser, layout, {"width": vw, "height": vh})
    login(page)
    page.wait_for_function("() => document.querySelector('[data-jellyportal-icon]')", timeout=25000)
    shots = SHOTS / "player"
    shots.mkdir(parents=True, exist_ok=True)
    phone = vw < 600

    # ---- the icon is there during playback
    start_playback(page, item, vw, vh)
    wait_icon_shown(page)
    info = icon_info(page)
    check(info is not None and info["count"] == 1, f"[{name}] exactly one icon during playback")
    check(info["inPlayerHeader"] and info["shown"] and info["w"] > 30 and info["h"] > 30, f"[{name}] it is in the player's header, visible, button-sized ({info['w']:.0f}x{info['h']:.0f})")
    check(info["y"] >= 0 and info["bottom"] <= 90 and info["right"] <= vw, f"[{name}] it is at the top of the screen, inside it")
    check(info["siblings"] >= 1 and info["leftOfOthers"], f"[{name}] it is the first of the right-hand buttons (left of cast and SyncPlay)")
    check(info["state"] == "ok", f"[{name}] it shows the portal's state (ok)")
    page.screenshot(path=str(shots / f"{name}-icon.png"))
    page.wait_for_timeout(3000)
    check(icon_info(page)["count"] == 1, f"[{name}] still one icon a few seconds into playback (the player re-renders its header)")

    # ---- the state follows the portal while playing
    scenario("down")
    page.wait_for_function("() => { const i = document.querySelector('[data-jellyportal-icon]'); return i && i.getAttribute('data-state') === 'down'; }", timeout=45000)
    wake(page, vw, vh)
    check(icon_info(page)["state"] == "down", f"[{name}] the icon turns red during playback when a service goes down")
    scenario("ok")
    page.wait_for_function("() => { const i = document.querySelector('[data-jellyportal-icon]'); return i && i.getAttribute('data-state') === 'ok'; }", timeout=45000)
    wake(page, vw, vh)

    # ---- the bubble opens over the picture
    hash_before = page.evaluate("location.hash")
    page.click(ICON)
    page.wait_for_selector(BUBBLE)
    page.wait_for_timeout(500)
    box = page.locator(BUBBLE).bounding_box()
    ib = icon_info(page)
    check(box["x"] >= 0 and box["x"] + box["width"] <= vw and box["y"] >= 0 and box["y"] + box["height"] <= vh, f"[{name}] the bubble is entirely on screen")
    check(box["y"] >= ib["bottom"] and abs(box["width"] - (vw - 16 if phone else 360)) <= 1, f"[{name}] it sits under the icon, {'screen minus 16 px' if phone else '360 px'} wide ({box['width']:.0f})")
    check(page.evaluate("location.hash") == hash_before and on_player(page), f"[{name}] opening it does not navigate (URL is still {hash_before})")
    t0 = video_state(page)
    page.wait_for_timeout(1500)
    t1 = video_state(page)
    check(not t1["paused"] and t1["t"] > t0["t"], f"[{name}] the video keeps playing under the open bubble")
    top = page.evaluate("(s) => { const b = document.querySelector(s); const r = b.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + 30); return !!e && b.contains(e); }", BUBBLE)
    check(top, f"[{name}] the bubble is above the player's own layers (the point under it hits the bubble)")
    page.screenshot(path=str(shots / f"{name}-bubble.png"))

    # ---- Escape closes the bubble and nothing else: jellyfin-web's own Escape would also hide the player's controls
    page.keyboard.press("Escape")
    page.wait_for_timeout(600)
    check(not bubble_open(page), f"[{name}] Escape closes the bubble")
    check(on_player(page) and not video_state(page)["paused"], f"[{name}] and the video is still playing on the player page")
    check(icon_info(page)["shown"], f"[{name}] and that is all it does: the player's controls are still shown (jellyfin-web's own Escape hides them)")
    wake(page, vw, vh)

    # ---- the controls fade by themselves; the bubble does not depend on them
    page.click(ICON)
    page.wait_for_selector(BUBBLE)
    page.wait_for_timeout(5500)
    check(bubble_open(page), f"[{name}] an open bubble stays open when the player's controls fade out")
    check(not icon_info(page)["shown"], f"[{name}] the icon fades with the player's controls")
    page.screenshot(path=str(shots / f"{name}-bubble-controls-faded.png"))
    page.keyboard.press("Escape")
    page.wait_for_timeout(600)
    check(not bubble_open(page), f"[{name}] Escape closes the bubble with the controls faded")
    check(on_player(page) and not video_state(page)["paused"], f"[{name}] and the video is still playing on the player page")
    wake(page, vw, vh)
    wait_icon_shown(page)
    check(icon_info(page)["shown"], f"[{name}] the icon comes back with the controls")

    # ---- a click on the picture: baseline, then with a bubble open
    px, py = vw // 2, vh // 2
    page.mouse.click(px, py)
    page.wait_for_timeout(500)
    baseline_paused = video_state(page)["paused"]
    check(baseline_paused, f"[{name}] baseline: a plain click on the picture pauses the video (so the next check can fail)")
    page.mouse.click(px, py)
    page.wait_for_timeout(500)
    check(not video_state(page)["paused"], f"[{name}] baseline: another click resumes it")
    wake(page, vw, vh)
    page.click(ICON)
    page.wait_for_selector(BUBBLE)
    page.mouse.click(px, py + 60)
    page.wait_for_timeout(600)
    check(not bubble_open(page), f"[{name}] a click on the picture closes the bubble")
    check(on_player(page) and not video_state(page)["paused"], f"[{name}] and does not also pause the video")
    wake(page, vw, vh)
    page.click(ICON)
    page.wait_for_selector(BUBBLE)
    page.click(".btnPause:visible")
    page.wait_for_timeout(600)
    check(not bubble_open(page) and video_state(page)["paused"], f"[{name}] a click on a control (Pause) with the bubble open closes it and still works")
    page.click(".btnPause:visible, .btnPlayPause:visible")
    page.wait_for_timeout(400)
    page.click(ICON)
    page.wait_for_selector(BUBBLE)
    page.click(ICON)
    page.wait_for_timeout(300)
    check(not bubble_open(page), f"[{name}] a second click on the icon closes the bubble")

    # ---- leaving the player and coming back, several times
    for round_number in range(1, 4):
        page.go_back()
        page.wait_for_function("() => location.hash.startsWith('#/details')", timeout=20000)
        page.wait_for_timeout(800)
        left = icon_info(page)
        check(left is not None and left["count"] == 1 and left["shown"] and not left["inPlayerHeader"],
              f"[{name}] round {round_number}: back in the ordinary header, one icon, not inside the player's")
        if round_number < 3:
            start_playback(page, item, vw, vh)
            wait_icon_shown(page)
            check(icon_info(page)["count"] == 1 and icon_info(page)["inPlayerHeader"], f"[{name}] round {round_number}: and in the player's header again, still one")
    check(not rec.problems_of_ours(), f"[{name}] nothing of ours in the console ({rec.problems_of_ours()})")
    ctx.close()


set_language()    # nothing here checks wording, but the screenshots should not depend on the run before
scenario("ok")
item_id = ensure_video()
with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=chrome(), headless=True, args=["--autoplay-policy=no-user-gesture-required"])
    for combo in (("modern-desktop", "modern", 1280, 720), ("legacy-desktop", "desktop-legacy", 1280, 720),
                  ("modern-phone", "modern", 390, 844), ("legacy-phone", "mobile-legacy", 390, 844)):
        if os.environ.get("PLAYER_ONLY") and os.environ["PLAYER_ONLY"] != combo[0]:
            continue   # PLAYER_ONLY=modern-desktop runs one disposition (about a minute), for a quick check
        run_combo(browser, item_id, *combo)
    browser.close()

print(f"\n{passes} checks passed, {len(failures)} failed")
for line in failures:
    print("  FAILED: " + line)
sys.exit(1 if failures else 0)
