#!/usr/bin/env python3
"""Build the installable plugin zip and print its MD5.

The zip holds the artifacts listed in build.yaml plus a meta.json (the same fields Jellyfin's own
plugin tooling writes). It is deterministic: same inputs, same bytes, so the checksum never
depends on when the zip was made.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path

import yaml

VERSION_RE = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
FIXED_ZIP_TIME = (1980, 1, 1, 0, 0, 0)


class PackageError(ValueError):
    """The plugin cannot be packaged as asked."""


def load_build_yaml(path: Path) -> dict:
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    for key in ("name", "guid", "targetAbi", "owner", "category", "overview", "description", "artifacts"):
        if not data.get(key):
            raise PackageError(f"{path} has no '{key}'")
    return data


def _clean(text: str) -> str:
    return " ".join(str(text).split())


def build_meta(build: dict, version: str, changelog: str, timestamp: str) -> dict:
    return {
        "category": build["category"],
        "changelog": changelog,
        "description": _clean(build["description"]),
        "guid": build["guid"],
        "name": build["name"],
        "overview": _clean(build["overview"]),
        "owner": build["owner"],
        "targetAbi": build["targetAbi"],
        "timestamp": timestamp,
        "version": version,
    }


def zip_name(build: dict, version: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", build["name"].lower()).strip("-")
    return f"{slug}_{version}.zip"


def md5_of(path: Path) -> str:
    digest = hashlib.md5()  # Jellyfin's plugin installer verifies an MD5; this is integrity, not security.
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def package(build_yaml: Path, publish_dir: Path, out_dir: Path, version: str,
            changelog: str, timestamp: str) -> tuple[Path, str]:
    if not VERSION_RE.fullmatch(version):
        raise PackageError(f"'{version}' is not a four-part Jellyfin version (X.Y.Z.N)")
    build = load_build_yaml(build_yaml)
    artifacts = [str(a) for a in build["artifacts"]]

    files: dict[str, bytes] = {}
    for name in artifacts:
        source = publish_dir / name
        if not source.is_file():
            raise PackageError(f"artifact '{name}' listed in build.yaml was not found in {publish_dir}")
        files[name] = source.read_bytes()

    # The version must really be in the binary: a build that ignored -p:Version would otherwise
    # ship a plugin whose DLL says 0.0.0.0 under a manifest that says something else.
    main_dll = artifacts[0]
    if version.encode("utf-16-le") not in files[main_dll]:
        raise PackageError(
            f"{main_dll} does not contain the version {version}. Was it built with -p:Version={version}?"
        )

    meta = build_meta(build, version, changelog, timestamp)
    files["meta.json"] = (json.dumps(meta, indent=2, ensure_ascii=False) + "\n").encode("utf-8")

    out_dir.mkdir(parents=True, exist_ok=True)
    target = out_dir / zip_name(build, version)
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(files):
            info = zipfile.ZipInfo(name, FIXED_ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, files[name])
    return target, md5_of(target)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", required=True, help="four-part Jellyfin version, e.g. 0.1.0.1")
    parser.add_argument("--publish-dir", required=True, type=Path)
    parser.add_argument("--build-yaml", default=Path("build.yaml"), type=Path)
    parser.add_argument("--out-dir", required=True, type=Path)
    parser.add_argument("--changelog-file", type=Path, help="markdown changelog to embed in meta.json")
    parser.add_argument("--timestamp", help="ISO 8601 UTC, default: now")
    args = parser.parse_args(argv)

    changelog = args.changelog_file.read_text(encoding="utf-8").strip() if args.changelog_file else ""
    timestamp = args.timestamp or datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    try:
        target, checksum = package(args.build_yaml, args.publish_dir, args.out_dir, args.version, changelog, timestamp)
    except PackageError as err:
        print(f"::error::{err}", file=sys.stderr)
        return 1
    print(f"zip={target}")
    print(f"md5={checksum}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
