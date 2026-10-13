"""The fake portal must be indistinguishable in shape from the real one.

`Jellyfin.Plugin.JellyPortal.Tests/Fixtures/portal-1.12.0/` holds real answers captured from the portal's own
route. These tests compare the fake's answers with them (keys, key order, timestamp format), so the fake cannot
drift from the contract while the plugin is developed against it. Run: python -m unittest discover -s dev/tests
"""
import http.client
import json
import re
import sys
import threading
import unittest
from http.server import ThreadingHTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "dev"))
import fake_portal  # noqa: E402

FIXTURES = ROOT / "Jellyfin.Plugin.JellyPortal.Tests" / "Fixtures" / "portal-1.12.0"
STAMP = re.compile(r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$")
QUERY = "/api/device/summary?sections=services,incidents,maintenance,announcements&services=all&detail=full"


def real(name):
    return json.loads((FIXTURES / name).read_text(encoding="utf-8"))


def shape(value):
    """Keys in order, recursively; a list is the union of its items (so optional keys like `ms` count)."""
    if isinstance(value, dict):
        return {k: shape(v) for k, v in value.items()}
    if isinstance(value, list):
        merged: dict = {}
        for item in value:
            s = shape(item)
            if isinstance(s, dict):
                for k, v in s.items():
                    merged.setdefault(k, v)
        return [merged] if merged else []
    return type(value).__name__


def key_order(value):
    if isinstance(value, dict):
        return [(k, key_order(v)) for k, v in value.items()]
    if isinstance(value, list) and value and isinstance(value[0], dict):
        return [key_order(value[0])]
    return None


def stamps(value, path=""):
    if isinstance(value, dict):
        for k, v in value.items():
            yield from stamps(v, f"{path}.{k}")
    elif isinstance(value, list):
        for i, v in enumerate(value):
            yield from stamps(v, f"{path}[{i}]")
    elif isinstance(value, str) and path.rsplit(".", 1)[-1] in ("now", "since", "starts", "ends"):
        yield path, value


class ShapeTests(unittest.TestCase):
    def test_every_ok_scenario_has_the_keys_and_key_order_of_the_real_answer(self):
        expected = real("full_scenario.json")
        for name in fake_portal.OK_SCENARIOS:
            with self.subTest(scenario=name):
                body = fake_portal.build(name)
                self.assertEqual(list(body), list(expected))
                for section in ("services", "incidents", "maintenance", "announcements"):
                    self.assertEqual(list(body[section]), list(expected[section]), section)
                    # An empty list has no item to compare; a service's `ms` is optional in the real one too.
                    for item in body[section]["items"]:
                        wanted = list(expected[section]["items"][0] if section != "services" else item)
                        if section == "services":
                            self.assertIn(list(item), (["name", "status"], ["name", "status", "ms"]))
                        else:
                            self.assertEqual(list(item), wanted, f"{section} item")

    def test_the_busiest_scenario_matches_the_real_answer_exactly_keys_order_and_types(self):
        fake, real_answer = fake_portal.build("busy"), real("full_scenario.json")
        self.assertEqual(shape(fake), shape(real_answer))
        self.assertEqual(key_order(fake), key_order(real_answer))

    def test_the_outdated_scenario_matches_what_a_real_older_portal_sends(self):
        fake, real_answer = fake_portal.build("busy", full=False), real("default_scenario.json")
        self.assertNotIn("detail", fake)
        self.assertEqual(shape(fake), shape(real_answer))
        self.assertEqual(key_order(fake), key_order(real_answer))

    def test_every_timestamp_is_utc_with_a_z(self):
        for name in fake_portal.OK_SCENARIOS:
            for path, value in stamps(fake_portal.build(name)):
                self.assertRegex(value, STAMP, f"{name}{path}")

    def test_services_are_worst_first_like_the_real_answer(self):
        for name in fake_portal.OK_SCENARIOS:
            ranks = [fake_portal.RANK[i["status"]] for i in fake_portal.build(name)["services"]["items"]]
            self.assertEqual(ranks, sorted(ranks), name)

    def test_counts_add_up_and_totals_are_the_totals(self):
        for name in fake_portal.OK_SCENARIOS:
            s = fake_portal.build(name)["services"]
            self.assertEqual(s["total"], sum(s[k] for k in fake_portal.COUNT_ORDER), name)
            self.assertEqual(s["total"], len(s["items"]), name)

    def test_the_marker_is_the_fifth_key(self):
        self.assertEqual(list(fake_portal.build("ok"))[:5], ["v", "now", "site", "overall", "detail"])


class ServerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        fake_portal.Handler.key = "k" * 20
        fake_portal.Handler.scenario = "ok"
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), fake_portal.Handler)
        cls.server.daemon_threads = True
        cls.port = cls.server.server_address[1]
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()

    def get(self, path=QUERY, key="k" * 20, method="GET"):
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        conn.request(method, path, headers={"X-Api-Key": key} if key else {})
        response = conn.getresponse()
        return response.status, response.read()

    def test_the_plugins_exact_request_is_answered(self):
        status, body = self.get()
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)["detail"], "full")

    def test_a_wrong_or_missing_key_is_a_401(self):
        self.assertEqual(self.get(key="nope")[0], 401)
        self.assertEqual(self.get(key=None)[0], 401)

    def test_the_key_is_not_accepted_in_the_query_string(self):
        self.assertEqual(self.get(QUERY + "&key=" + "k" * 20, key=None)[0], 401)

    def test_asking_for_the_inside_of_the_server_is_refused_loudly(self):
        for extra in ("&resources=all", "&jellyfin=1", "&vms=1"):
            self.assertEqual(self.get(QUERY + extra)[0], 400, extra)
        self.assertEqual(self.get(QUERY.replace("announcements", "announcements,resources"))[0], 400)
        self.assertEqual(self.get(QUERY.replace("announcements", "announcements,vms"))[0], 400)

    def test_any_other_query_is_refused_too(self):
        self.assertEqual(self.get("/api/device/summary")[0], 400)
        self.assertEqual(self.get(QUERY.replace("&detail=full", ""))[0], 400)

    def test_the_scenario_can_be_switched_at_runtime(self):
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        conn.request("POST", "/__scenario/down")
        self.assertEqual(conn.getresponse().status, 200)
        self.assertEqual(self.get()[0], 200)
        self.assertEqual(json.loads(self.get()[1])["services"]["down"], 1)
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        conn.request("POST", "/__scenario/disabled")
        conn.getresponse().read()
        self.assertEqual(self.get()[0], 404)
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        conn.request("POST", "/__scenario/bad_json")
        conn.getresponse().read()
        status, body = self.get()
        self.assertEqual(status, 200)
        self.assertNotIn(b'"v"', body)
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        conn.request("POST", "/__scenario/nonsense")
        self.assertEqual(conn.getresponse().status, 404)
        conn = http.client.HTTPConnection("127.0.0.1", self.port, timeout=5)
        conn.request("POST", "/__scenario/ok")
        conn.getresponse().read()


if __name__ == "__main__":
    unittest.main()
