#!/usr/bin/env python3
"""Write the markdown changelog of a release from the conventional commits since the previous tag.

The previous tag is the highest tag below this one, except that a stable release is compared with
the previous *stable* release, so it lists everything its release candidates went through.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys

from release_info import TAG_RE, TagError, parse_tag, version_key

SECTIONS = (("feat", "Features"), ("fix", "Bug fixes"), ("perf", "Performance"))
OTHER_TYPES = {"refactor", "docs", "revert", "build"}  # shown; chore/ci/test/style are noise
COMMIT_RE = re.compile(r"^(?P<type>[a-z]+)(?:\((?P<scope>[^)]*)\))?(?P<bang>!)?: (?P<text>.+)$")
RECORD, FIELD = "\x1e", "\x1f"


def git(repo: str, *args: str) -> str:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True, text=True).stdout


def previous_tag(tag: str, repo: str = ".") -> str | None:
    info = parse_tag(tag)
    candidates = [t for t in git(repo, "tag", "--list", "v*").split() if TAG_RE.fullmatch(t)]
    if not info["prerelease"]:
        candidates = [t for t in candidates if not parse_tag(t)["prerelease"]]
    below = [t for t in candidates if version_key(t) < version_key(tag)]
    return max(below, key=version_key) if below else None


def commits(tag: str, previous: str | None, repo: str = ".") -> list[dict]:
    revision = f"{previous}..{tag}" if previous else tag
    raw = git(repo, "log", "--no-merges", f"--format=%h{FIELD}%s{FIELD}%b{RECORD}", revision)
    result = []
    for record in raw.split(RECORD):
        if not record.strip():
            continue
        sha, subject, body = (record.strip("\n").split(FIELD) + ["", ""])[:3]
        result.append({"sha": sha.strip(), "subject": subject.strip(), "body": body})
    return result


def render(tag: str, previous: str | None, items: list[dict], compare_base: str | None = None) -> str:
    grouped: dict[str, list[str]] = {name: [] for _, name in SECTIONS}
    other: list[str] = []
    breaking: list[str] = []
    for item in items:
        match = COMMIT_RE.match(item["subject"])
        if not match:
            other.append(f"- {item['subject']} ({item['sha']})")
            continue
        kind, scope, text = match["type"], match["scope"], match["text"]
        line = f"- {scope + ': ' if scope else ''}{text} ({item['sha']})"
        if match["bang"] or "BREAKING CHANGE:" in item["body"]:
            breaking.append(line)
        for key, name in SECTIONS:
            if kind == key:
                grouped[name].append(line)
                break
        else:
            if kind in OTHER_TYPES:
                other.append(line)
    blocks = []
    if breaking:
        blocks.append("### Breaking changes\n" + "\n".join(breaking))
    for _, name in SECTIONS:
        if grouped[name]:
            blocks.append(f"### {name}\n" + "\n".join(grouped[name]))
    if other:
        blocks.append("### Other changes\n" + "\n".join(other))
    if not blocks:
        blocks.append("No user-facing changes.")
    if compare_base and previous:
        blocks.append(f"**Full changelog**: {compare_base}/compare/{previous}...{tag}")
    return "\n\n".join(blocks) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("tag")
    parser.add_argument("--repo", default=".")
    parser.add_argument("--compare-base", help="e.g. https://github.com/OWNER/REPO, adds a compare link")
    args = parser.parse_args(argv)
    try:
        previous = previous_tag(args.tag, args.repo)
        sys.stdout.write(render(args.tag, previous, commits(args.tag, previous, args.repo), args.compare_base))
    except (TagError, subprocess.CalledProcessError) as err:
        print(f"::error::{err}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
