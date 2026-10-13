#!/usr/bin/env bash
# Local development Jellyfin 12.2 in Docker. Listens on 127.0.0.1 only; data lives in dev/data/
# (gitignored). This is never the owner's production server.
#
#   dev/jellyfin.sh up       start (creates the container on first use)
#   dev/jellyfin.sh down     stop and remove the container, keep dev/data
#   dev/jellyfin.sh restart  restart the container
#   dev/jellyfin.sh logs     follow the server log
#   dev/jellyfin.sh reset    remove the container AND dev/data (a fresh server)
set -euo pipefail
cd "$(dirname "$0")/.."

NAME=jellyportal-dev
IMAGE=jellyfin/jellyfin:12.2
PORT="${JELLYPORTAL_DEV_PORT:-8096}"
DATA="$PWD/dev/data"

case "${1:-}" in
  up)
    mkdir -p "$DATA/config" "$DATA/cache" "$DATA/media"
    if docker ps -a --format '{{.Names}}' | grep -qx "$NAME"; then
      docker start "$NAME" >/dev/null
    else
      docker run -d --name "$NAME" --user "$(id -u):$(id -g)" \
        -p "127.0.0.1:$PORT:8096" \
        -v "$DATA/config:/config" -v "$DATA/cache:/cache" -v "$DATA/media:/media" \
        "$IMAGE" >/dev/null
    fi
    echo "Jellyfin starting on http://127.0.0.1:$PORT"
    ;;
  down)    docker rm -f "$NAME" >/dev/null && echo "removed $NAME" ;;
  restart) docker restart "$NAME" >/dev/null && echo "restarted $NAME" ;;
  logs)    docker logs -f "$NAME" ;;
  reset)
    docker rm -f "$NAME" >/dev/null 2>&1 || true
    rm -rf "$DATA/config" "$DATA/cache"
    echo "dev server reset"
    ;;
  *) sed -n '2,10p' "$0"; exit 1 ;;
esac
