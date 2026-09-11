#!/usr/bin/env bash
#
# Construye la imagen y deja en dist/ lo que se sube al servidor:
#
#   diarspeicher-image.tar.gz   la imagen ya construida
#   docker-compose.yml          sin la seccion build: en el servidor no se compila
#   .env                        la configuracion
#
# La arquitectura sale de ARCHITECTURE en .env.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="$ROOT/dist"
IMAGE="diarspeicher:latest"

cd "$ROOT"

# shellcheck disable=SC1091
ARCHITECTURE="$(grep -E '^ARCHITECTURE=' .env | tail -n 1 | cut -d= -f2- | tr -d '"'"'"' \r ')"
: "${ARCHITECTURE:?Falta ARCHITECTURE en .env}"

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }

log "Construyendo la imagen ($ARCHITECTURE)"
docker buildx build \
    --platform "$ARCHITECTURE" \
    --pull \
    --tag "$IMAGE" \
    --load \
    .

log "Preparando dist/"
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

# El compose del servidor no puede llevar build:, porque alli no hay fuentes.
# Se genera a partir del de desarrollo para no mantener dos ficheros a mano.
python3 - "$ROOT/docker-compose.yml" "$DIST_DIR/docker-compose.yml" <<'PY'
import re
import sys

source, target = sys.argv[1], sys.argv[2]
with open(source, encoding="utf-8") as handle:
    lines = handle.readlines()

kept, skipping = [], False
for line in lines:
    stripped = line.strip()
    if stripped.startswith("build:"):
        skipping = True
        continue
    if skipping:
        # El bloque build: termina en la primera clave con su misma indentacion o menor.
        if re.match(r"^\s{0,8}\S", line) and not stripped.startswith(("context:", "dockerfile:")):
            skipping = False
        else:
            continue
    if stripped.startswith("pull_policy:"):
        continue
    kept.append(line)

with open(target, "w", encoding="utf-8") as handle:
    handle.writelines(kept)
PY

cp .env "$DIST_DIR/.env"

log "Exportando la imagen"
docker save "$IMAGE" | gzip > "$DIST_DIR/diarspeicher-image.tar.gz"

log "Listo"
ls -lh "$DIST_DIR"

cat <<TXT

En el servidor, con los tres ficheros de dist/ en el mismo directorio:

  docker load -i diarspeicher-image.tar.gz
  docker compose up -d --force-recreate

--force-recreate no es opcional mientras el cambio del volumen de bibliotecas
no este desplegado: un restart conserva el montaje anterior.
TXT
