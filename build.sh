#!/usr/bin/env bash
#
# Builds the image and leaves in dist/ what gets uploaded to the server:
#
#   diarspeicher-image.tar.gz   the built image
#   docker-compose.yml          without the build: section, nothing is compiled on the server
#   .env                        the configuration
#
# The architecture comes from ARCHITECTURE in .env.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="$ROOT/dist"
PUBLISH_DIR="$ROOT/publish"
IMAGE="diarspeicher:latest"

cd "$ROOT"

ARCHITECTURE="$(grep -E '^ARCHITECTURE=' .env | tail -n 1 | cut -d= -f2- | tr -d '"'"'"' \r ')"
: "${ARCHITECTURE:?Falta ARCHITECTURE en .env}"

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }

publish_native() {
    local target_arch rid

    target_arch="${ARCHITECTURE#*/}"
    case "$target_arch" in
        arm64) rid="linux-arm64" ;;
        amd64) rid="linux-x64" ;;
        *) echo "Arquitectura sin RID conocido: $ARCHITECTURE" >&2; exit 1 ;;
    esac

    if ! command -v clang >/dev/null 2>&1; then
        echo "Falta clang. Instala: sudo apt-get install -y clang llvm" >&2
        exit 1
    fi
    if [ "$rid" = "linux-arm64" ] && [ "$(uname -m)" != "aarch64" ] && ! command -v aarch64-linux-gnu-gcc >/dev/null 2>&1; then
        echo "Falta la toolchain de aarch64. Instala: sudo apt-get install -y binutils-aarch64-linux-gnu gcc-aarch64-linux-gnu" >&2
        exit 1
    fi

    log "Compilando el binario nativo ($rid)"
    rm -rf "$PUBLISH_DIR"
    dotnet publish src/DiarSpeicher.Api/DiarSpeicher.Api.csproj -c Release -r "$rid" -o "$PUBLISH_DIR"
    find "$PUBLISH_DIR" -name '*.dbg' -delete
}

publish_native

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

# The server compose cannot carry build:, there are no sources there. It is generated from the
# development one so two files are not maintained by hand.
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
        # The build: block ends at the first key with the same or lower indentation.
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
