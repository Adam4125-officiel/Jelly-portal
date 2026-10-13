#!/usr/bin/env python3
"""Validate a release tag and derive everything the release workflow needs from it.

Tag format (strict):   vX.Y.Z-rc.N  (a release candidate, N >= 1)   or   vX.Y.Z  (stable)

Jellyfin wants four-part numeric versions with no suffix, so:
    vX.Y.Z-rc.N  ->  X.Y.Z.N
    vX.Y.Z       ->  X.Y.Z.0

The version comes from the tag and nowhere else.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys

TAG_RE = re.compile(r"^v(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$")

# .NET refuses an assembly/file version part above 65534.
MAX_PART = 65534


class TagError(ValueError):
    """The tag is not an acceptable release tag."""


def parse_tag(tag: str) -> dict:
    match = TAG_RE.fullmatch(tag)
    if not match:
        raise TagError(
            f"'{tag}' is not a release tag. Expected vX.Y.Z-rc.N (release candidate) or vX.Y.Z (stable), "
            "matching ^v\\d+\\.\\d+\\.\\d+(-rc\\.\\d+)?$"
        )
    major, minor, patch = (int(match.group(i)) for i in (1, 2, 3))
    rc = int(match.group(4)) if match.group(4) is not None else None
    if rc is not None and rc < 1:
        raise TagError(
            f"'{tag}': release candidates start at rc.1. rc.0 would map to X.Y.Z.0, "
            "the same Jellyfin version as the stable release."
        )
    for part in (major, minor, patch, rc or 0):
        if part > MAX_PART:
            raise TagError(f"'{tag}': version numbers above {MAX_PART} cannot be used as a .NET assembly version.")
    base = f"{major}.{minor}.{patch}"
    return {
        "tag": tag,
        "base": base,
        "rc": rc,
        "prerelease": rc is not None,
        "jellyfin_version": f"{base}.{rc if rc is not None else 0}",
    }


def version_key(tag: str) -> tuple:
    """Sort key in release order: 1.2.3-rc.1 < 1.2.3-rc.2 < 1.2.3 < 1.2.4-rc.1."""
    info = parse_tag(tag)
    major, minor, patch = (int(p) for p in info["base"].split("."))
    return (major, minor, patch, 0 if info["prerelease"] else 1, info["rc"] or 0)


def rc_tags_on_commit(base: str, commit: str, repo: str = ".") -> list[str]:
    out = subprocess.run(
        ["git", "tag", "--points-at", commit],
        cwd=repo, check=True, capture_output=True, text=True,
    ).stdout.split()
    pattern = re.compile(rf"^v{re.escape(base)}-rc\.[1-9][0-9]*$")
    return sorted(t for t in out if pattern.fullmatch(t))


def check_stable_has_rc(info: dict, commit: str, repo: str = ".") -> list[str]:
    """A stable tag is only valid on a commit that already carries an RC tag of the same version."""
    if info["prerelease"]:
        return []
    found = rc_tags_on_commit(info["base"], commit, repo)
    if not found:
        raise TagError(
            f"{info['tag']} is on a commit with no v{info['base']}-rc.N tag. A stable release can only be "
            f"tagged on the exact commit of a release candidate: tag v{info['base']}-rc.N first, test it, "
            f"then tag v{info['base']} on that same commit."
        )
    return found


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("tag")
    parser.add_argument("--check-rc-on", metavar="COMMIT",
                        help="for a stable tag, require an RC tag of the same version on this commit")
    parser.add_argument("--repo", default=".")
    args = parser.parse_args(argv)
    try:
        info = parse_tag(args.tag)
        if args.check_rc_on:
            check_stable_has_rc(info, args.check_rc_on, args.repo)
    except TagError as err:
        print(f"::error::{err}", file=sys.stderr)
        return 1
    for key in ("tag", "base", "prerelease", "jellyfin_version"):
        value = info[key]
        print(f"{key}={str(value).lower() if isinstance(value, bool) else value}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
