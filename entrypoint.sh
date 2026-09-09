#!/bin/sh
set -e

IS_ROOT=false
[ "$(id -u)" = "0" ] && IS_ROOT=true

as_root() {
    if [ "$IS_ROOT" = true ]; then
        "$@"
    fi
}

echo "==> DiarSpeicher: asegurando estructura y permisos en /data..."

mkdir -p /data/db /data/thumbnails /data/cache/pages /data/backups

as_root chown -R node:node /data
as_root chmod -R u+rwX,g+rwX,o+rX /data

DOTNET_EXTRACT="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-/tmp/.net}"
mkdir -p "$DOTNET_EXTRACT"
as_root chown -R node:node "$DOTNET_EXTRACT"

umask 0002

if [ "$IS_ROOT" = true ]; then
    echo "==> Permisos listos en /data. Desescalando a node (1000:1000)..."
    exec setpriv --reuid=1000 --regid=1000 --init-groups "$@"
fi

exec "$@"
