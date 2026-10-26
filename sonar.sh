#!/usr/bin/env bash
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

SALIDA="$ROOT/.sonar-build.log"

printf '\033[1;36m==> Analizando (la primera vez descarga el paquete y tarda)\033[0m\n'
dotnet build DiarSpeicher.slnx --no-incremental -p:Sonar=true -v:minimal > "$SALIDA" 2>&1
CODIGO=$?

EXCLUIR_TESTS='/[Tt]ests?/|\.test\.cs\('

LIMPIO="$(mktemp)"
grep -vE "($EXCLUIR_TESTS).*warning (S[0-9]+|CA[0-9]+):" "$SALIDA" | sed "/^Build succeeded\.$/,\$d" > "$LIMPIO"
mv "$LIMPIO" "$SALIDA"

if [ "$CODIGO" -ne 0 ]; then
    printf '\033[1;31m==> El build FALLO (codigo %s). No hay analisis que interpretar.\033[0m\n\n' "$CODIGO"
    grep -oE "error [A-Z]+[0-9]+: [^[]*" "$SALIDA" | sort -u | head -20
    printf '\n    Log completo: ./sonar.sh --log\n'
    exit 1
fi


avisos_de_codigo() {
    grep -E 'warning (S[0-9]+|CA[0-9]+):' "$SALIDA" | grep -vE "$EXCLUIR_TESTS"
}

descripcion() {
    case "$1" in
        S107) echo "Mas de 7 parametros en un metodo" ;;
        S108) echo "Bloque anidado vacio" ;;
        S134) echo "Estructuras de control anidadas a mas de 3 niveles" ;;
        S138) echo "Metodo demasiado largo" ;;
        S1067) echo "Mas de 3 operadores condicionales en una expresion" ;;
        S1104) echo "Campo publico: debe ser propiedad" ;;
        S1144) echo "Miembro privado sin usar (codigo muerto)" ;;
        S1151) echo "Bloque case demasiado largo" ;;
        S1192) echo "Literal de texto repetido: definir una constante" ;;
        S1200) echo "Clase acoplada a demasiados tipos" ;;
        S1541) echo "Complejidad ciclomatica mayor a 10" ;;
        S1994) echo "Contador de for modificado fuera de su incremento" ;;
        S2245) echo "Generador pseudoaleatorio no seguro" ;;
        S2325) echo "Metodo que puede ser estatico" ;;
        S2930) echo "IDisposable sin liberar" ;;
        S3218) echo "Miembro anidado oculta uno de la clase externa" ;;
        S3267) echo "Bucle que debe simplificarse con LINQ" ;;
        S3358) echo "Operador ternario anidado" ;;
        S3398) echo "Metodo privado que debe ir dentro de la clase que lo usa" ;;
        S3776) echo "Complejidad cognitiva mayor a 15" ;;
        S5693) echo "Tamano maximo de peticion sin limitar" ;;
        S6444) echo "Regex sin timeout" ;;
        S6966) echo "Metodo asincrono sin await" ;;
        S100) echo "Nombre de metodo sin PascalCase" ;;
        S101) echo "Nombre de tipo sin PascalCase" ;;
        S117) echo "Nombre de variable local sin camelCase" ;;
        S125) echo "Codigo comentado" ;;
        S1075) echo "URI o ruta escrita en el codigo" ;;
        S1117) echo "Variable local oculta un campo" ;;
        S1118) echo "Clase de utilidades con constructor publico: hacerla static" ;;
        S1121) echo "Asignacion dentro de una subexpresion" ;;
        S1125) echo "Literal booleano redundante" ;;
        S1135) echo "Etiqueta TODO pendiente" ;;
        S1172) echo "Parametro sin usar" ;;
        S1186) echo "Metodo vacio" ;;
        S1450) echo "Campo privado que debe ser variable local" ;;
        S1481) echo "Variable local sin usar" ;;
        S1854) echo "Asignacion cuyo valor nunca se lee" ;;
        S1871) echo "Dos ramas con la misma implementacion" ;;
        S1905) echo "Cast redundante" ;;
        S1940) echo "Condicion booleana invertida innecesariamente" ;;
        S2077) echo "SQL construido por concatenacion" ;;
        S2259) echo "Posible desreferencia de null" ;;
        S2292) echo "Propiedad con campo de respaldo: usar auto-propiedad" ;;
        S2333) echo "Modificador redundante" ;;
        S2365) echo "Propiedad que copia o crea una coleccion en cada acceso" ;;
        S2386) echo "Campo estatico publico mutable" ;;
        S3052) echo "Miembro inicializado con su valor por defecto" ;;
        S3168) echo "async void: debe devolver Task" ;;
        S3235) echo "Parentesis redundantes" ;;
        S3242) echo "Parametro con tipo mas especifico del necesario" ;;
        S3260) echo "Clase privada que debe ser sealed" ;;
        S3459) echo "Miembro sin asignar" ;;
        S3626) echo "Salto redundante" ;;
        S3881) echo "Patron IDisposable mal implementado" ;;
        S4136) echo "Sobrecargas que no estan seguidas" ;;
        S4144) echo "Metodos con implementacion identica" ;;
        S4457) echo "Metodo async que valida argumentos dentro de la tarea" ;;
        S4487) echo "Campo privado que se escribe pero no se lee" ;;
        S5332) echo "Protocolo sin cifrar (http, ftp)" ;;
        S6602) echo "Usar Find en lugar de FirstOrDefault" ;;
        S6603) echo "Usar TrueForAll en lugar de All" ;;
        S6605) echo "Usar Exists en lugar de Any" ;;
        S6608) echo "Usar indexador en lugar de ElementAt" ;;
        S6609) echo "Usar Max o Min de la coleccion en lugar de LINQ" ;;
        S6610) echo "Usar StartsWith o EndsWith con char" ;;
        S6612) echo "Usar GetOrAdd o equivalente atomico del diccionario" ;;
        S6613) echo "Usar indexador en lugar de First" ;;
        S6617) echo "Usar Contains en lugar de Any" ;;
        S6618) echo "Usar string.Create en lugar de FormattableString" ;;
        S6640) echo "Codigo unsafe" ;;
        *) avisos_de_codigo | grep -m1 -E "warning $1:" | sed -E 's/.*warning [A-Z0-9]+: //; s/ \[[^]]*\]$//' ;;
    esac
}

AVISOS="$(avisos_de_codigo | sed -E 's/ \[[^]]*\]$//' | sort -u | grep -oE 'warning (S[0-9]+|CA[0-9]+):' | sed 's/warning //; s/://' | sort)"

case "${1:-}" in
    --log)
        cat "$SALIDA"
        ;;
    --full)
        avisos_de_codigo | sed 's|^/[^ ]*/DiarSpeicher/||' | sort -u
        ;;
    "")
        if [ -z "$AVISOS" ]; then
            printf '\033[1;32m==> Analizador cargado y cero avisos.\033[0m\n'
            exit "$CODIGO"
        fi
        printf '\n\033[1;36m==> Avisos por regla\033[0m\n\n'
        echo "$AVISOS" | uniq -c | sort -rn | while read -r cantidad regla; do
            printf '%7s  %-6s  %s\n' "$cantidad" "$regla" "$(descripcion "$regla")"
        done
        printf '\n\033[1;36m==> Total: %s avisos, %s reglas distintas\033[0m\n' \
            "$(echo "$AVISOS" | wc -l)" "$(echo "$AVISOS" | uniq | wc -l)"
        printf '    Detalle de una regla:  ./sonar.sh S3776\n'
        ;;
    *)
        avisos_de_codigo | grep -E "warning $1:" | sed 's|^/[^ ]*/DiarSpeicher/||' | sort -u
        ;;
esac

exit 0
