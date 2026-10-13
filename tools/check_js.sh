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

# The settings page carries its script inline; check what is between its <script> tags too.
page=Jellyfin.Plugin.JellyPortal/Configuration/configPage.html
tmp="$(mktemp --suffix=.js)"
trap 'rm -f "$tmp"' EXIT
node -e '
  const fs = require("fs");
  const html = fs.readFileSync(process.argv[1], "utf8");
  const blocks = [...html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)].map((m) => m[1]);
  if (blocks.length === 0) { console.error("::error::No inline <script> found in " + process.argv[1]); process.exit(1); }
  fs.writeFileSync(process.argv[2], blocks.join("\n;\n"));
' "$page" "$tmp"
node --check "$tmp"
echo "ok  $page (inline script)"
