#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="$ROOT/src/DiarSpeicher.Client/bin/publish"

cd "$ROOT"

rm -rf "$PUBLISH_DIR"
dotnet publish src/DiarSpeicher.Client/DiarSpeicher.Client.csproj -c Release -o "$PUBLISH_DIR"

printf '\nSube el contenido de:\n\n  %s/wwwroot/\n' "$PUBLISH_DIR"
