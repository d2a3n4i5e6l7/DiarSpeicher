#!/usr/bin/env bash
#
# Analisis de Sonar sin servidor: el mismo motor de reglas que SonarQube, como
# analizador de Roslyn.
#
#   ./sonar.sh            resumen por regla, de mayor a menor
#   ./sonar.sh S3776      detalle de una regla concreta
#   ./sonar.sh --full     todos los avisos, sin agrupar
#   ./sonar.sh --log      la salida cruda del build, para depurar

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

SALIDA="$ROOT/.sonar-build.log"

printf '\033[1;36m==> Analizando (la primera vez descarga el paquete y tarda)\033[0m\n'
dotnet build DiarSpeicher.slnx --no-incremental -p:Sonar=true -v:normal > "$SALIDA" 2>&1
CODIGO=$?

if [ "$CODIGO" -ne 0 ]; then
    printf '\033[1;31m==> El build FALLO (codigo %s). No hay analisis que interpretar.\033[0m\n\n' "$CODIGO"
    grep -oE "error [A-Z]+[0-9]+: [^[]*" "$SALIDA" | sort -u | head -20
    printf '\n    Log completo: ./sonar.sh --log\n'
    exit 1
fi

if ! grep -qE "SonarAnalyzer\\.(CSharp\\.)?dll|analyzer.*[Ss]onar" "$SALIDA"; then
    printf '\033[1;33m==> Aviso: no veo el analizador en el log; el recuento puede no significar nada.\033[0m\n\n'
fi

AVISOS="$(grep -oE 'warning (S[0-9]+|CA[0-9]+):' "$SALIDA" | sed 's/warning //; s/://' | sort)"

case "${1:-}" in
    --log)
        cat "$SALIDA"
        ;;
    --full)
        grep -E 'warning (S[0-9]+|CA[0-9]+):' "$SALIDA" | sed 's|^.*/DiarSpeicher|DiarSpeicher|' | sort -u
        ;;
    "")
        if [ -z "$AVISOS" ]; then
            printf '\033[1;32m==> Analizador cargado y cero avisos.\033[0m\n'
            exit "$CODIGO"
        fi
        printf '\n\033[1;36m==> Avisos por regla\033[0m\n\n'
        echo "$AVISOS" | uniq -c | sort -rn
        printf '\n\033[1;36m==> Total: %s avisos, %s reglas distintas\033[0m\n' \
            "$(echo "$AVISOS" | wc -l)" "$(echo "$AVISOS" | uniq | wc -l)"
        printf '    Detalle de una regla:  ./sonar.sh S3776\n'
        ;;
    *)
        grep -E "warning $1:" "$SALIDA" | sed 's|^.*/DiarSpeicher|DiarSpeicher|' | sort -u
        ;;
esac

exit 0
