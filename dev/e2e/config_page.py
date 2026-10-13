"""End to end: the plugin's settings page in the Jellyfin dashboard, in a real browser.

Checks the page loads and is filled, that it never receives the portal API key, that "test the connection" says
the right thing for each failure of the fake portal, that saving works, keeps the key when the field is left
blank, refuses a bad address, and that nothing the page does raises a console error of ours.
Needs: dev/jellyfin.sh up, dev/dev.sh, the fake portal on 172.17.0.1:8099.
"""
import json
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from common import BASE, PORTAL_KEY, SHOTS, Recorder, chrome, login, strip_ws  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

FAKE = "http://172.17.0.1:8099"
PAGE = f"{BASE}/web/#/configurationpage?name=Jelly%20Portal"
failures: list[str] = []


def check(ok: bool, what: str) -> None:
    print(("PASS " if ok else "FAIL ") + what)
    if not ok:
        failures.append(what)


def scenario(name: str) -> None:
    urllib.request.urlopen(urllib.request.Request(f"{FAKE}/__scenario/{name}", method="POST")).read()


def reset_settings() -> None:
    """Start from known settings whatever the last run left behind (through the same API the page uses)."""
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    import jf
    token = jf.login()
    status, body = jf.call("POST", "/JellyPortal/Config", {
        "portalUrl": FAKE, "apiKey": PORTAL_KEY, "clearApiKey": False, "adminsOnly": False, "priorityServices": "",
        "soonWindowHours": 24, "pollSeconds": 60, "cacheSeconds": 30}, token)
    assert status == 200, (status, body)


reset_settings()
with sync_playwright() as p:
    browser = p.chromium.launch(executable_path=chrome(), headless=True)
    page = browser.new_context(viewport={"width": 1280, "height": 1000}).new_page()
    rec = Recorder(page)
    scenario("ok")
    login(page)
    page.goto(PAGE, wait_until="domcontentloaded")
    page.wait_for_selector("#jpPortalUrl", timeout=30000)
    page.wait_for_function("() => document.querySelector('#jpFileTransformation').textContent.length > 0", timeout=15000)
    SHOTS.mkdir(exist_ok=True)
    page.screenshot(path=str(SHOTS / "config-page.png"), full_page=True)

    check(page.input_value("#jpPortalUrl") == "http://172.17.0.1:8099", "the saved portal address is shown")
    check(page.input_value("#jpApiKey") == "", "the API key field is EMPTY (the page never has the key)")
    check("clé enregistrée" in (page.get_attribute("#jpApiKey", "placeholder") or ""), "the field says a key is saved")
    check(page.is_visible("#jpClearKey"), "the 'clear the saved key' button is offered")
    check("est installé" in page.inner_text("#jpFileTransformation"), "File Transformation is reported as installed")
    check(page.input_value("#jpPoll") == "60" and page.input_value("#jpCache") == "30" and page.input_value("#jpSoon") == "24", "the numeric settings are filled")
    check(page.get_attribute("#jpPoll", "min") == "10" and page.get_attribute("#jpCache", "min") == "5", "the allowed ranges come from the server")

    page.click("#jpTest")
    page.wait_for_function("() => document.querySelector('#jpTestResult').textContent.includes('Connexion réussie')", timeout=20000)
    check("« Dev Portal »" in page.inner_text("#jpTestResult") and "8 services" in page.inner_text("#jpTestResult"), "test connection: success names the portal and counts services")

    expected = {
        "bad_key": "refuse la clé API",
        "disabled": "API « Display device » est désactivée",
        "outdated": "1.12.0",
        "bad_json": "inattendu",
        "bad_gateway": "Impossible de joindre",
    }
    for name, fragment in expected.items():
        scenario(name)
        page.click("#jpTest")
        page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(fragment)})", timeout=20000)
        check(True, f"test connection ({name}): says \"...{fragment}...\"")
    scenario("ok")

    # An address typed but not saved is what the test uses.
    page.fill("#jpPortalUrl", "http://172.17.0.1:9")
    page.click("#jpTest")
    page.wait_for_function("() => document.querySelector('#jpTestResult').textContent.includes('Impossible de joindre')", timeout=20000)
    check(True, "test connection uses the address as typed, saved or not")
    page.fill("#jpPortalUrl", "http://172.17.0.1:8099")

    # Save: change things, leave the key blank.
    page.fill("#jpPoll", "5")          # below the minimum the page declares: the browser itself refuses to submit
    check(page.evaluate("() => document.querySelector('#jpPoll').checkValidity()") is False, "5 s is refused by the browser's own validation (min comes from the server)")
    page.fill("#jpSoon", "36")
    page.fill("#jpPoll", "15")
    page.fill("#jpPriority", "Jellyfin\nSeerr\n")
    page.select_option("#jpAdminsOnly", "false")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function("() => document.querySelector('#jpSaveResult').textContent.includes('Enregistré')", timeout=20000)
    check(page.input_value("#jpSoon") == "36", "saved: the window is 36 h")
    check(page.input_value("#jpPoll") == "15", "saved: the polling interval is 15 s")
    check(page.input_value("#jpPriority") == "Jellyfin\nSeerr", "saved: the priority list is normalised")
    check(page.input_value("#jpApiKey") == "" and page.is_visible("#jpClearKey"), "saved with the key field blank: the key is still there")

    # A bad address is refused with a sentence, and nothing changes.
    page.fill("#jpPortalUrl", "portal.example")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function("() => document.querySelector('#jpSaveResult').textContent.includes('pas valide')", timeout=20000)
    check("http://" in page.inner_text("#jpSaveResult"), "a bad address is refused with a sentence that says what to type")
    page.screenshot(path=str(SHOTS / "config-page-error.png"), full_page=True)
    page.reload(wait_until="domcontentloaded")
    page.wait_for_selector("#jpPortalUrl")
    page.wait_for_function("() => document.querySelector('#jpPortalUrl').value.length > 0", timeout=15000)
    check(page.input_value("#jpPortalUrl") == "http://172.17.0.1:8099", "after the refusal the saved address is unchanged")

    # Clear the key, save, then the connection test reports the missing key; put it back through the page.
    page.click("#jpClearKey")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function("() => document.querySelector('#jpSaveResult').textContent.includes('Enregistré')", timeout=20000)
    check(not page.is_visible("#jpClearKey") and page.get_attribute("#jpApiKey", "placeholder") in ("", None), "clearing erased the key")
    page.click("#jpTest")
    page.wait_for_function("() => document.querySelector('#jpTestResult').textContent.includes('clé API')", timeout=20000)
    check(True, "without a key the test says to enter the API key")
    page.fill("#jpApiKey", PORTAL_KEY)
    page.click("#jpForm button[type=submit]")
    page.wait_for_function("() => document.querySelector('#jpSaveResult').textContent.includes('Enregistré')", timeout=20000)
    check(page.input_value("#jpApiKey") == "" and page.is_visible("#jpClearKey"), "after saving a new key the field empties again and a key is saved")
    page.click("#jpTest")
    page.wait_for_function("() => document.querySelector('#jpTestResult').textContent.includes('Connexion réussie')", timeout=20000)
    check(True, "the new key works")

    check(any(t == "info" and "client script loaded" in m for t, m in rec.console), "the injected client script ran in the dashboard too")
    check(rec.leaked(PORTAL_KEY) == [], f"the key appears in NO response body the browser received: {rec.leaked(PORTAL_KEY)}")
    check(PORTAL_KEY not in page.content(), "the key is not in the page DOM")
    check(rec.problems_of_ours() == [], f"no console error/warning, page error or failed request of ours: {rec.problems_of_ours()}")
    page.screenshot(path=str(SHOTS / "config-page-final.png"), full_page=True)
    browser.close()

print(f"\n{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
