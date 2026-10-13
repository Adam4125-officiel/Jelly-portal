#!/usr/bin/env python3
"""Add a released plugin zip to the Jellyfin repository manifests.

A release candidate goes into the unstable manifest only; a stable release goes into both.
Each manifest keeps the 10 highest versions. Everything about the version (number, changelog,
target ABI, timestamp) is read from the meta.json inside the zip, and the checksum is computed
from the zip itself, so a manifest entry cannot disagree with the file it points at.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path

KEEP_VERSIONS = 10
PLUGIN_FIELDS = ("guid", "name", "description", "overview", "owner", "category")


class ManifestError(ValueError):
    """The manifest cannot be updated as asked."""


def version_tuple(version: str) -> tuple[int, ...]:
    return tuple(int(part) for part in version.split("."))


def read_meta(zip_path: Path) -> dict:
    with zipfile.ZipFile(zip_path) as archive:
        try:
            return json.loads(archive.read("meta.json"))
        except KeyError:
            raise ManifestError(f"{zip_path} has no meta.json") from None


def md5_of(path: Path) -> str:
    digest = hashlib.md5()  # Jellyfin verifies an MD5 on install.
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def load_manifest(path: Path) -> list:
    if not path.exists():
        return []
    data = json.loads(path.read_text(encoding="utf-8") or "[]")
    if not isinstance(data, list):
        raise ManifestError(f"{path} is not a JSON list of plugins")
    return data


def version_entry(meta: dict, source_url: str, checksum: str) -> dict:
    return {
        "version": meta["version"],
        "changelog": meta["changelog"],
        "targetAbi": meta["targetAbi"],
        "sourceUrl": source_url,
        "checksum": checksum,
        "timestamp": meta["timestamp"],
    }


def add_version(manifest: list, meta: dict, entry: dict, keep: int = KEEP_VERSIONS) -> list:
    plugin = next((p for p in manifest if str(p.get("guid", "")).lower() == meta["guid"].lower()), None)
    if plugin is None:
        plugin = {"versions": []}
        manifest.append(plugin)
    plugin.update({field: meta[field] for field in PLUGIN_FIELDS})
    versions = [v for v in plugin.get("versions", []) if v.get("version") != entry["version"]]
    versions.append(entry)
    versions.sort(key=lambda v: version_tuple(v["version"]), reverse=True)
    # Keep the key order stable so a commit to the manifest branch only shows what really changed.
    ordered = {field: plugin[field] for field in PLUGIN_FIELDS}
    ordered["versions"] = versions[:keep]
    plugin.clear()
    plugin.update(ordered)
    return manifest


def write_manifest(path: Path, manifest: list) -> None:
    path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def update(zip_path: Path, source_url: str, prerelease: bool, stable: Path, unstable: Path) -> list[Path]:
    meta = read_meta(zip_path)
    entry = version_entry(meta, source_url, md5_of(zip_path))
    targets = [unstable] if prerelease else [stable, unstable]
    for target in targets:
        write_manifest(target, add_version(load_manifest(target), meta, entry))
    return targets


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--zip", required=True, type=Path)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--prerelease", required=True, choices=("true", "false"))
    parser.add_argument("--stable-manifest", default=Path("manifest.json"), type=Path)
    parser.add_argument("--unstable-manifest", default=Path("manifest-unstable.json"), type=Path)
    args = parser.parse_args(argv)
    try:
        written = update(args.zip, args.source_url, args.prerelease == "true",
                         args.stable_manifest, args.unstable_manifest)
    except (ManifestError, OSError, zipfile.BadZipFile, json.JSONDecodeError) as err:
        print(f"::error::{err}", file=sys.stderr)
        return 1
    for path in written:
        print(f"updated {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
