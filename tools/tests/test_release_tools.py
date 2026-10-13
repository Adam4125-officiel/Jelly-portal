"""Tests for the release tooling. Run: python -m unittest discover -s tools/tests"""
import json
import os
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import changelog  # noqa: E402
import package_plugin  # noqa: E402
import release_info  # noqa: E402
import update_manifest  # noqa: E402

BUILD_YAML = """---
name: "Jelly Portal"
guid: "a72a6bca-0bfe-480c-946f-e0a3fcb2c746"
version: "0.0.0.0"
targetAbi: "12.2.0.0"
framework: "net10.0"
overview: "Short overview."
description: >
  A longer description
  over two lines.
category: "General"
owner: "someone"
artifacts:
- "Jellyfin.Plugin.JellyPortal.dll"
changelog: >
  placeholder
"""


def make_repo(root: Path) -> str:
    env = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@example.invalid",
           "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@example.invalid"}

    def run(*args):
        subprocess.run(["git", "-c", "commit.gpgsign=false", "-c", "tag.gpgsign=false", *args],
                       cwd=root, check=True, capture_output=True, env=env)

    run("init", "-q", "-b", "main")
    return str(root), run


class ReleaseInfoTests(unittest.TestCase):
    def test_stable_tag_maps_to_dot_zero(self):
        info = release_info.parse_tag("v1.2.3")
        self.assertEqual(info["jellyfin_version"], "1.2.3.0")
        self.assertFalse(info["prerelease"])

    def test_rc_tag_maps_to_its_number(self):
        info = release_info.parse_tag("v0.1.0-rc.4")
        self.assertEqual(info["jellyfin_version"], "0.1.0.4")
        self.assertTrue(info["prerelease"])

    def test_invalid_tags_are_rejected(self):
        for tag in ("v1.2", "1.2.3", "v1.2.3-rc", "v1.2.3-rc.", "v1.2.3-beta.1", "v1.2.3-rc.1-x",
                    "v1.2.3.4", "release-1", "v1.2.3\n", "V1.2.3", "v1.2.3-RC.1", "v1.2.3-rc.0",
                    "v1.2.99999", "v1.2.3-rc.70000"):
            with self.subTest(tag=tag), self.assertRaises(release_info.TagError):
                release_info.parse_tag(tag)

    def test_release_order(self):
        tags = ["v1.0.0", "v0.9.0", "v1.0.0-rc.2", "v1.0.0-rc.10", "v1.0.0-rc.1", "v1.0.1-rc.1"]
        self.assertEqual(sorted(tags, key=release_info.version_key),
                         ["v0.9.0", "v1.0.0-rc.1", "v1.0.0-rc.2", "v1.0.0-rc.10", "v1.0.0", "v1.0.1-rc.1"])

    def test_stable_requires_an_rc_on_the_same_commit(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, run = make_repo(Path(tmp))
            Path(tmp, "a").write_text("1")
            run("add", "."), run("commit", "-qm", "feat: one")
            run("tag", "v0.1.0-rc.1")
            Path(tmp, "a").write_text("2")
            run("add", "."), run("commit", "-qm", "fix: two")

            stable = release_info.parse_tag("v0.1.0")
            with self.assertRaises(release_info.TagError):
                release_info.check_stable_has_rc(stable, "HEAD", repo)  # the RC is one commit behind
            self.assertEqual(release_info.check_stable_has_rc(stable, "HEAD~1", repo), ["v0.1.0-rc.1"])
            run("tag", "v0.2.0-rc.1")
            with self.assertRaises(release_info.TagError):  # an RC of another version does not count
                release_info.check_stable_has_rc(release_info.parse_tag("v0.1.0"), "HEAD", repo)
            self.assertEqual(release_info.check_stable_has_rc(release_info.parse_tag("v0.2.0-rc.2"), "HEAD", repo), [])

    def test_annotated_rc_tags_count(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo, run = make_repo(Path(tmp))
            Path(tmp, "a").write_text("1")
            run("add", "."), run("commit", "-qm", "feat: one")
            run("tag", "-a", "v1.0.0-rc.1", "-m", "rc")
            self.assertEqual(release_info.rc_tags_on_commit("1.0.0", "HEAD", repo), ["v1.0.0-rc.1"])


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.build_yaml = self.root / "build.yaml"
        self.build_yaml.write_text(BUILD_YAML)
        self.publish = self.root / "publish"
        self.publish.mkdir()
        self.dll = self.publish / "Jellyfin.Plugin.JellyPortal.dll"
        self.dll.write_bytes(b"MZ-fake" + "0.1.0.1".encode("utf-16-le") + b"tail")

    def tearDown(self):
        self.tmp.cleanup()

    def build(self, out="out", version="0.1.0.1", timestamp="2026-01-01T00:00:00Z"):
        return package_plugin.package(self.build_yaml, self.publish, self.root / out, version, "notes", timestamp)

    def test_zip_contains_the_dll_and_a_meta_json(self):
        path, checksum = self.build()
        self.assertEqual(path.name, "jelly-portal_0.1.0.1.zip")
        self.assertRegex(checksum, r"^[0-9A-F]{32}$")
        with zipfile.ZipFile(path) as archive:
            self.assertEqual(archive.namelist(), ["Jellyfin.Plugin.JellyPortal.dll", "meta.json"])
            meta = json.loads(archive.read("meta.json"))
        self.assertEqual(meta["version"], "0.1.0.1")
        self.assertEqual(meta["targetAbi"], "12.2.0.0")
        self.assertEqual(meta["guid"], "a72a6bca-0bfe-480c-946f-e0a3fcb2c746")
        self.assertEqual(meta["description"], "A longer description over two lines.")
        self.assertEqual(meta["changelog"], "notes")

    def test_the_zip_is_reproducible(self):
        first = self.build("one")[1]
        second = self.build("two")[1]
        self.assertEqual(first, second)
        self.assertNotEqual(first, self.build("three", timestamp="2027-01-01T00:00:00Z")[1])

    def test_a_dll_that_ignored_the_version_is_refused(self):
        self.dll.write_bytes(b"MZ-fake" + "0.0.0.0".encode("utf-16-le"))
        with self.assertRaises(package_plugin.PackageError):
            self.build()

    def test_a_missing_artifact_is_refused(self):
        self.dll.unlink()
        with self.assertRaises(package_plugin.PackageError):
            self.build()

    def test_a_malformed_version_is_refused(self):
        for version in ("0.1.0", "0.1.0-rc.1", "v0.1.0.1", "0.1.0.1.2"):
            with self.subTest(version=version), self.assertRaises(package_plugin.PackageError):
                self.build(version=version)


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.stable = self.root / "manifest.json"
        self.unstable = self.root / "manifest-unstable.json"
        self.stable.write_text("[]\n")
        self.unstable.write_text("[]\n")

    def tearDown(self):
        self.tmp.cleanup()

    def make_zip(self, version, changelog="c"):
        path = self.root / f"jelly-portal_{version}.zip"
        meta = {"category": "General", "changelog": changelog, "description": "d", "guid": "A72A6BCA-0BFE-480C-946F-E0A3FCB2C746",
                "name": "Jelly Portal", "overview": "o", "owner": "me", "targetAbi": "12.2.0.0",
                "timestamp": "2026-01-01T00:00:00Z", "version": version}
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("meta.json", json.dumps(meta))
            archive.writestr("x.dll", version)
        return path

    def release(self, version, prerelease):
        update_manifest.update(self.make_zip(version), f"https://example.invalid/{version}.zip", prerelease, self.stable, self.unstable)

    def versions(self, path):
        data = json.loads(path.read_text())
        return [v["version"] for v in data[0]["versions"]] if data else []

    def test_an_rc_only_reaches_the_unstable_manifest(self):
        self.release("0.1.0.1", prerelease=True)
        self.assertEqual(self.versions(self.stable), [])
        self.assertEqual(self.versions(self.unstable), ["0.1.0.1"])

    def test_a_stable_release_reaches_both(self):
        self.release("0.1.0.1", prerelease=True)
        self.release("0.1.0.0", prerelease=False)
        self.assertEqual(self.versions(self.stable), ["0.1.0.0"])
        self.assertEqual(self.versions(self.unstable), ["0.1.0.1", "0.1.0.0"])

    def test_entry_shape(self):
        self.release("0.1.0.1", prerelease=True)
        entry = json.loads(self.unstable.read_text())[0]["versions"][0]
        self.assertEqual(list(entry), ["version", "changelog", "targetAbi", "sourceUrl", "checksum", "timestamp"])
        self.assertEqual(entry["sourceUrl"], "https://example.invalid/0.1.0.1.zip")
        self.assertRegex(entry["checksum"], r"^[0-9A-F]{32}$")
        plugin = json.loads(self.unstable.read_text())[0]
        self.assertEqual(plugin["guid"], "A72A6BCA-0BFE-480C-946F-E0A3FCB2C746")
        self.assertEqual(plugin["name"], "Jelly Portal")

    def test_republishing_a_version_replaces_it(self):
        self.release("0.1.0.1", prerelease=True)
        self.release("0.1.0.1", prerelease=True)
        self.assertEqual(self.versions(self.unstable), ["0.1.0.1"])
        self.assertEqual(len(json.loads(self.unstable.read_text())), 1)

    def test_only_the_ten_highest_versions_are_kept_in_numeric_order(self):
        for n in range(1, 13):
            self.release(f"0.2.0.{n}", prerelease=True)
        kept = self.versions(self.unstable)
        self.assertEqual(len(kept), 10)
        self.assertEqual(kept[0], "0.2.0.12")
        self.assertEqual(kept[-1], "0.2.0.3")  # numeric, not lexicographic: .10 > .9

    def test_a_zip_without_meta_json_is_refused(self):
        bare = self.root / "bare.zip"
        with zipfile.ZipFile(bare, "w") as archive:
            archive.writestr("x.dll", "x")
        with self.assertRaises(update_manifest.ManifestError):
            update_manifest.update(bare, "u", True, self.stable, self.unstable)


class ChangelogTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.repo, self.run = make_repo(Path(self.tmp.name))
        self.n = 0

    def tearDown(self):
        self.tmp.cleanup()

    def commit(self, subject, body=""):
        self.n += 1
        Path(self.tmp.name, "f").write_text(str(self.n))
        self.run("add", ".")
        self.run("commit", "-q", "-m", subject + (f"\n\n{body}" if body else ""))

    def text(self, tag):
        previous = changelog.previous_tag(tag, self.repo)
        return changelog.render(tag, previous, changelog.commits(tag, previous, self.repo), "https://example.invalid/r")

    def test_groups_by_type_and_hides_noise(self):
        self.commit("feat(panel): add the bubble")
        self.commit("fix: close on escape")
        self.commit("perf: cache the summary")
        self.commit("chore: bump things")
        self.commit("ci: tweak workflow")
        self.commit("docs: explain")
        self.commit("not conventional at all")
        self.run("tag", "v0.1.0-rc.1")
        out = self.text("v0.1.0-rc.1")
        self.assertIn("### Features\n- panel: add the bubble", out)
        self.assertIn("### Bug fixes\n- close on escape", out)
        self.assertIn("### Performance\n- cache the summary", out)
        self.assertIn("explain", out)
        self.assertIn("not conventional at all", out)
        self.assertNotIn("bump things", out)
        self.assertNotIn("tweak workflow", out)

    def test_breaking_changes_are_called_out(self):
        self.commit("feat!: drop the old setting")
        self.commit("fix: x", body="BREAKING CHANGE: renamed")
        self.run("tag", "v1.0.0-rc.1")
        out = self.text("v1.0.0-rc.1")
        self.assertIn("### Breaking changes", out)
        self.assertIn("drop the old setting", out.split("### Breaking changes")[1].split("###")[0])

    def test_an_rc_is_compared_with_the_previous_tag_and_a_stable_with_the_previous_stable(self):
        self.commit("feat: first")
        self.run("tag", "v0.1.0-rc.1")
        self.commit("fix: second")
        self.run("tag", "v0.1.0-rc.2")
        self.run("tag", "v0.1.0")
        self.assertEqual(changelog.previous_tag("v0.1.0-rc.2", self.repo), "v0.1.0-rc.1")
        self.assertIsNone(changelog.previous_tag("v0.1.0", self.repo))
        out = self.text("v0.1.0")
        self.assertIn("first", out)
        self.assertIn("second", out)  # the stable lists everything since nothing stable existed
        self.commit("feat: third")
        self.run("tag", "v0.2.0-rc.1")
        self.assertEqual(changelog.previous_tag("v0.2.0-rc.1", self.repo), "v0.1.0")
        out = self.text("v0.2.0-rc.1")
        self.assertIn("third", out)
        self.assertNotIn("first", out)
        self.assertIn("compare/v0.1.0...v0.2.0-rc.1", out)

    def test_an_empty_release_says_so(self):
        self.commit("chore: only noise")
        self.run("tag", "v0.0.1-rc.1")
        self.assertIn("No user-facing changes.", self.text("v0.0.1-rc.1"))

    def test_merge_commits_are_ignored(self):
        self.commit("feat: base")
        self.run("tag", "v0.1.0-rc.1")
        self.run("checkout", "-q", "-b", "feat/x")
        self.commit("feat: from the branch")
        self.run("checkout", "-q", "main")
        self.run("merge", "--no-ff", "-q", "-m", "Merge pull request #1 from x/y", "feat/x")
        self.run("tag", "v0.1.0-rc.2")
        out = self.text("v0.1.0-rc.2")
        self.assertIn("from the branch", out)
        self.assertNotIn("Merge pull request", out)


if __name__ == "__main__":
    unittest.main()
