#!/usr/bin/env bash
#
# Aplica formato a las dos mitades del repositorio:
#
#   backend   dotnet format sobre DiarSpeicher.slnx
#   frontend  eslint --fix sobre frontEnd/
#
# Escribe en los ficheros. Para comprobar sin tocar nada, usa lint.sh.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }

log "Backend: dotnet format"
dotnet format DiarSpeicher.slnx

log "Frontend: eslint --fix"
# pnpm resuelve el paquete desde el directorio actual, y el package.json vive en
# frontEnd/, no en la raiz: sin -C falla con ERR_PNPM_NO_PKG_MANIFEST.
pnpm -C frontEnd exec eslint . --fix

log "Formato aplicado"
