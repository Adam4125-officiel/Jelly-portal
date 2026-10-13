#!/usr/bin/env bash
# The development loop: build -> package like a release -> install into the dev Jellyfin -> restart ->
# show what the plugin logged.
#
#   dev/dev.sh            all of the above
#   dev/dev.sh --no-logs  without the log excerpt
#
# The plugin is packaged by tools/package_plugin.py, i.e. the exact path a release takes, so a
# packaging problem shows up here and not on a tag. Dev builds are version 0.0.0.1.
#
# The dev Jellyfin must NOT have the Jelly Portal repository configured (`dev/jf.py repos` with only
# File Transformation's): plugins auto-update by default, and at startup Jellyfin would replace this
# 0.0.0.1 build with the newest release from that repository. To test a real release instead, add the
# unstable repository on purpose and install from the catalog.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$HOME/.local/node/bin:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

VERSION=0.0.0.1
BUILD=dev/data/build
PLUGINS=dev/data/config/plugins

rm -rf "$BUILD" && mkdir -p "$BUILD"
dotnet publish Jellyfin.Plugin.JellyPortal/Jellyfin.Plugin.JellyPortal.csproj -c Release \
  -o "$BUILD/publish" -p:Version="$VERSION" --nologo -v quiet
python3 tools/package_plugin.py --version "$VERSION" --publish-dir "$BUILD/publish" --out-dir "$BUILD" >/dev/null

docker stop jellyportal-dev >/dev/null 2>&1 || true
# Only one Jelly Portal may be installed: a catalog-installed release next to this build would load twice.
find "$PLUGINS" -maxdepth 1 -name 'Jelly Portal_*' -exec rm -rf {} + 2>/dev/null || true
mkdir -p "$PLUGINS/Jelly Portal_$VERSION"
unzip -qo "$BUILD"/jelly-portal_"$VERSION".zip -d "$PLUGINS/Jelly Portal_$VERSION"
[ -f "$PLUGINS/configurations/Jellyfin.Plugin.JellyPortal.xml" ] || python3 dev/jf.py configure

dev/jellyfin.sh up >/dev/null
python3 dev/jf.py wait >/dev/null
echo "installed Jelly Portal $VERSION and restarted Jellyfin"
if [ "${1:-}" != "--no-logs" ]; then
  docker logs jellyportal-dev 2>&1 | grep -E "JellyPortal|Jelly Portal|\[(WRN|ERR|FTL)\].*(Plugin|plugin)" | sed 's/^\[[^]]*\] //' | tail -15 || true
fi
