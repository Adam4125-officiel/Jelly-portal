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
from common import BASE, LANGUAGE, PORTAL_KEY, SHOTS, Recorder, chrome, login, strip_ws  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

FAKE = "http://172.17.0.1:8099"
PUBLIC = "https://status.dev.example"

# What the page says, in the language of the run (JP_LANG, English by default).
ALL = {
    "en": {"key_saved": "key saved", "installed": "is installed", "test_ok": "Connection successful", "portal_name": "\u201cDev Portal\u201d", "services": "8 services",
           "bad_key": "refuses the API key", "disabled": "\u201cDisplay device\u201d API is switched off", "bad_json": "unexpected", "unreachable": "cannot be reached",
           "saved": "Saved", "invalid": "is not valid", "public": "public", "need_key": "API key", "save_button": "Save", "visible_label": "Visible to"},
    "fr": {"key_saved": "cl\u00e9 enregistr\u00e9e", "installed": "est install\u00e9", "test_ok": "Connexion r\u00e9ussie", "portal_name": "\u00ab Dev Portal \u00bb", "services": "8 services",
           "bad_key": "refuse la cl\u00e9 API", "disabled": "API \u00ab Display device \u00bb est d\u00e9sactiv\u00e9e", "bad_json": "inattendu", "unreachable": "Impossible de joindre",
           "saved": "Enregistr\u00e9", "invalid": "pas valide", "public": "publique", "need_key": "cl\u00e9 API", "save_button": "Enregistrer", "visible_label": "Visible par"},
}
OTHER = "fr" if LANGUAGE == "en" else "en"
W, V = ALL[LANGUAGE], ALL[OTHER]    # this run's wording, and the other language's
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
        "portalUrl": FAKE, "publicUrl": PUBLIC, "apiKey": PORTAL_KEY, "clearApiKey": False, "adminsOnly": False, "priorityServices": "", "language": LANGUAGE,
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
    check(W["key_saved"] in (page.get_attribute("#jpApiKey", "placeholder") or ""), "the field says a key is saved")
    check(page.is_visible("#jpClearKey"), "the 'clear the saved key' button is offered")
    check(W["installed"] in page.inner_text("#jpFileTransformation"), "File Transformation is reported as installed")
    check(page.input_value("#jpPoll") == "60" and page.input_value("#jpCache") == "30" and page.input_value("#jpSoon") == "24", "the numeric settings are filled")
    check(page.get_attribute("#jpPoll", "min") == "10" and page.get_attribute("#jpCache", "min") == "5", "the allowed ranges come from the server")

    page.click("#jpTest")
    page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(W['test_ok'])})", timeout=20000)
    check(W["portal_name"] in page.inner_text("#jpTestResult") and W["services"] in page.inner_text("#jpTestResult"), "test connection: success names the portal and counts services")

    expected = {
        "bad_key": W["bad_key"],
        "disabled": W["disabled"],
        "outdated": "1.12.0",
        "bad_json": W["bad_json"],
        "bad_gateway": W["unreachable"],
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
    page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(W['unreachable'])})", timeout=20000)
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
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['saved'])})", timeout=20000)
    check(page.input_value("#jpSoon") == "36", "saved: the window is 36 h")
    check(page.input_value("#jpPoll") == "15", "saved: the polling interval is 15 s")
    check(page.input_value("#jpPriority") == "Jellyfin\nSeerr", "saved: the priority list is normalised")
    check(page.input_value("#jpApiKey") == "" and page.is_visible("#jpClearKey"), "saved with the key field blank: the key is still there")

    # A bad address is refused with a sentence, and nothing changes.
    page.fill("#jpPortalUrl", "portal.example")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['invalid'])})", timeout=20000)
    check("http://" in page.inner_text("#jpSaveResult"), "a bad address is refused with a sentence that says what to type")
    (SHOTS / LANGUAGE).mkdir(parents=True, exist_ok=True)
    page.screenshot(path=str(SHOTS / LANGUAGE / "config-page-error.png"), full_page=True)
    page.reload(wait_until="domcontentloaded")
    page.wait_for_selector("#jpPortalUrl")
    page.wait_for_function("() => document.querySelector('#jpPortalUrl').value.length > 0", timeout=15000)
    check(page.input_value("#jpPortalUrl") == "http://172.17.0.1:8099", "after the refusal the saved address is unchanged")

    # The public address (the bubble's link) is its own setting: shown, refused when malformed, removable, saved.
    check(page.input_value("#jpPublicUrl") == PUBLIC, "the saved public address is shown, apart from the address the portal is asked with")
    page.fill("#jpPublicUrl", "ftp://status.example")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['invalid'])})", timeout=20000)
    check(W["public"] in page.inner_text("#jpSaveResult") and "https://" in page.inner_text("#jpSaveResult"), "a malformed public address is refused with a sentence that names it and says what to type")
    page.fill("#jpPublicUrl", "")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['saved'])})", timeout=20000)
    page.reload(wait_until="domcontentloaded")
    page.wait_for_selector("#jpPortalUrl")
    page.wait_for_function("() => document.querySelector('#jpPortalUrl').value.length > 0", timeout=15000)
    check(page.input_value("#jpPublicUrl") == "", "a blank public address is saved as blank (no link)")
    page.fill("#jpPublicUrl", PUBLIC)
    page.click("#jpForm button[type=submit]")
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['saved'])})", timeout=20000)
    check(page.input_value("#jpPublicUrl") == PUBLIC and page.input_value("#jpPortalUrl") == "http://172.17.0.1:8099", "the public address is saved and the other address is untouched")

    # The language: previewed as soon as it is chosen (a message already on screen included), kept when saved, refused when
    # it does not exist, and "auto" follows what the dashboard itself is displayed in.
    save_button = "#jpForm button[type=submit]"
    check(page.input_value("#jpLanguage") == LANGUAGE and W["save_button"] in page.inner_text(save_button), f"the selector shows the saved language and the page speaks it ({LANGUAGE})")
    page.click("#jpTest")
    page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(W['test_ok'])})", timeout=20000)
    page.select_option("#jpLanguage", OTHER)
    page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(V['test_ok'])})", timeout=5000)
    check(V["save_button"] in page.inner_text(save_button) and V["visible_label"] in page.inner_text("label[for=jpAdminsOnly]"), "choosing another language rewrites the whole page at once, before anything is saved")
    check(V["test_ok"] in page.inner_text("#jpTestResult"), "and a message already on screen is rewritten too")
    page.reload(wait_until="domcontentloaded")
    page.wait_for_selector("#jpPortalUrl")
    page.wait_for_function("() => document.querySelector('#jpPortalUrl').value.length > 0", timeout=15000)
    check(page.input_value("#jpLanguage") == LANGUAGE and W["save_button"] in page.inner_text(save_button), "a choice that was not saved is gone after a reload")
    page.select_option("#jpLanguage", OTHER)
    page.click(save_button)
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(V['saved'])})", timeout=20000)
    page.reload(wait_until="domcontentloaded")
    page.wait_for_selector("#jpPortalUrl")
    page.wait_for_function("() => document.querySelector('#jpPortalUrl').value.length > 0", timeout=15000)
    check(page.input_value("#jpLanguage") == OTHER and V["save_button"] in page.inner_text(save_button), f"a saved language is still there after a reload ({OTHER})")
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    import jf
    token = jf.login()
    status, body = jf.call("POST", "/JellyPortal/Config", {"language": "klingon"}, token)
    refusal = json.loads(body) if isinstance(body, str) else body    # an error answer comes back as text
    check(status == 400 and refusal.get("error") == "invalid_language", f"the server refuses a language that does not exist (HTTP {status}, {body})")
    _, view = jf.call("GET", "/JellyPortal/Config", token=token)
    check(view["language"] == OTHER, "and the refusal changed nothing")
    page.select_option("#jpLanguage", "auto")
    page.click(save_button)
    page.wait_for_function("() => document.querySelector('#jpSaveResult').textContent.length > 0", timeout=20000)
    for locale, expect in (("fr-FR", "fr"), ("en-US", "en"), ("de-DE", "en")):
        ctx2 = browser.new_context(viewport={"width": 1280, "height": 1000}, locale=locale)
        page2 = ctx2.new_page()
        login(page2)
        page2.goto(PAGE, wait_until="domcontentloaded")
        page2.wait_for_selector("#jpPortalUrl", timeout=30000)
        page2.wait_for_function("() => document.querySelector('#jpPortalUrl').value.length > 0", timeout=15000)
        check(ALL[expect]["save_button"] in page2.inner_text(save_button) and page2.input_value("#jpLanguage") == "auto",
              f"auto: a browser in {locale} gets the page in {'French' if expect == 'fr' else 'English'}")
        ctx2.close()
    page.select_option("#jpLanguage", LANGUAGE)
    page.click(save_button)
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['saved'])})", timeout=20000)

    # Clear the key, save, then the connection test reports the missing key; put it back through the page.
    page.click("#jpClearKey")
    page.click("#jpForm button[type=submit]")
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['saved'])})", timeout=20000)
    check(not page.is_visible("#jpClearKey") and page.get_attribute("#jpApiKey", "placeholder") in ("", None), "clearing erased the key")
    page.click("#jpTest")
    page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(W['need_key'])})", timeout=20000)
    check(True, "without a key the test says to enter the API key")
    page.fill("#jpApiKey", PORTAL_KEY)
    page.click("#jpForm button[type=submit]")
    page.wait_for_function(f"() => document.querySelector('#jpSaveResult').textContent.includes({json.dumps(W['saved'])})", timeout=20000)
    check(page.input_value("#jpApiKey") == "" and page.is_visible("#jpClearKey"), "after saving a new key the field empties again and a key is saved")
    page.click("#jpTest")
    page.wait_for_function(f"() => document.querySelector('#jpTestResult').textContent.includes({json.dumps(W['test_ok'])})", timeout=20000)
    check(True, "the new key works")

    check(any(t == "info" and "client script loaded" in m for t, m in rec.console), "the injected client script ran in the dashboard too")
    check(rec.leaked(PORTAL_KEY) == [], f"the key appears in NO response body the browser received: {rec.leaked(PORTAL_KEY)}")
    check(PORTAL_KEY not in page.content(), "the key is not in the page DOM")
    check(rec.problems_of_ours() == [], f"no console error/warning, page error or failed request of ours: {rec.problems_of_ours()}")
    page.screenshot(path=str(SHOTS / "config-page-final.png"), full_page=True)
    browser.close()

print(f"\n{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
