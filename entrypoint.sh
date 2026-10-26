#!/bin/sh
set -e

# Uid and gid the application ends up running as.
#
# Configurable because some systems only have root (CoreELEC and LibreELEC among them), and
# there the directories of mounted disks belong to root with 755 permissions. A process
# dropped to another uid could only read them, and creating a folder in a library would fail
# with "Access to the path is denied". PUID=0 keeps the process as root.
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

umask 0002

if [ "$IS_ROOT" = true ] && [ "$PUID" != "0" ]; then
    echo "==> Permisos listos en /data. Desescalando a ${PUID}:${PGID}..."
    exec setpriv --reuid="$PUID" --regid="$PGID" --init-groups "$@"
fi

if [ "$IS_ROOT" = true ]; then
    echo "==> Permisos listos en /data. PUID=0: el proceso sigue como root."
fi

exec "$@"
