#!/usr/bin/env bash
#
# Formats both halves of the repository:
#
#   backend   dotnet format on DiarSpeicher.slnx
#   frontend  eslint --fix on frontEnd/
#
# Writes to the files. To check without touching anything, use lint.sh.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$1"; }

log "Backend: dotnet format"
dotnet format DiarSpeicher.slnx

log "Frontend: eslint --fix"
# pnpm resolves the package from the current directory, and package.json lives in frontEnd/, not
# at the root: without -C it fails with ERR_PNPM_NO_PKG_MANIFEST.
pnpm -C frontEnd exec eslint . --fix

log "Formato aplicado"
