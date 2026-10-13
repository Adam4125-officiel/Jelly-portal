#!/usr/bin/env bash
# Syntax-check every client script the plugin serves to the Jellyfin web UI.
# Fails when there is nothing to check, so this can never pass vacuously.
set -euo pipefail
cd "$(dirname "$0")/.."

mapfile -t files < <(find Jellyfin.Plugin.JellyPortal/Web -type f -name '*.js' | sort)
if [ "${#files[@]}" -eq 0 ]; then
  echo "::error::No client JavaScript found under Jellyfin.Plugin.JellyPortal/Web" >&2
  exit 1
fi
for file in "${files[@]}"; do
  node --check "$file"
  echo "ok  $file"
done
