#!/bin/sh
set -e

# Uid y gid con los que acaba corriendo la aplicacion.
#
# Son configurables porque hay sistemas donde solo existe root -CoreELEC y LibreELEC entre
# ellos- y ahi los directorios de los discos montados pertenecen a root con permisos 755. Un
# proceso desescalado a otro uid solo podria leerlos, y crear una carpeta en una biblioteca
# fallaria con "Access to the path is denied". PUID=0 deja el proceso como root.
PUID="${PUID:-1000}"
PGID="${PGID:-1000}"

IS_ROOT=false
[ "$(id -u)" = "0" ] && IS_ROOT=true

as_root() {
    if [ "$IS_ROOT" = true ]; then
        "$@"
    fi
}

echo "==> DiarSpeicher: asegurando estructura y permisos en /data..."

mkdir -p /data/db /data/thumbnails /data/backups /data/manga_database

as_root chown -R "$PUID:$PGID" /data
as_root chmod -R u+rwX,g+rwX,o+rX /data

mkdir -p /tmp
as_root chmod 1777 /tmp
as_root chown -R "$PUID:$PGID" /tmp

DOTNET_EXTRACT="${DOTNET_BUNDLE_EXTRACT_BASE_DIR:-/tmp/.net}"
mkdir -p "$DOTNET_EXTRACT"
as_root chown -R "$PUID:$PGID" "$DOTNET_EXTRACT"

umask 0002

if [ "$IS_ROOT" = true ] && [ "$PUID" != "0" ]; then
    echo "==> Permisos listos en /data. Desescalando a ${PUID}:${PGID}..."
    exec setpriv --reuid="$PUID" --regid="$PGID" --init-groups "$@"
fi

if [ "$IS_ROOT" = true ]; then
    echo "==> Permisos listos en /data. PUID=0: el proceso sigue como root."
fi

exec "$@"
