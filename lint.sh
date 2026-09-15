#!/usr/bin/env bash
#
# Verifica las dos mitades del repositorio sin escribir en ningun fichero:
#
#   backend   dotnet format --verify-no-changes  +  build con warnings como errores
#   frontend  eslint
#
# Sale con codigo distinto de cero si algo falla. Para corregir, usa format.sh.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

STATUS=0

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }
fail() { printf '\033[1;31m    FALLO: %s\033[0m\n' "$1"; STATUS=1; }

log "Backend: formato"
dotnet format DiarSpeicher.slnx --verify-no-changes || fail "dotnet format (corrige con ./format.sh)"

log "Backend: analizadores"
dotnet build DiarSpeicher.slnx --no-incremental -warnaserror || fail "dotnet build"

log "Frontend: eslint"
pnpm -C frontEnd exec eslint . || fail "eslint (corrige con ./format.sh)"

if [ "$STATUS" -eq 0 ]; then
    printf '\n\033[1;32m==> Todo limpio\033[0m\n'
else
    printf '\n\033[1;31m==> Hay fallos\033[0m\n'
fi

exit "$STATUS"
