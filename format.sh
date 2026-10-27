#!/usr/bin/env bash
#
# Formats the repository:
#
#   backend   dotnet format on DiarSpeicher.slnx
#
# Writes to the files. To check without touching anything, use lint.sh.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }

log "Backend: dotnet format"
dotnet format DiarSpeicher.slnx

log "Formato aplicado"
