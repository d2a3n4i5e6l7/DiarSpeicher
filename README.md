# DiarSpeicher

Servidor autoalojado de cómics y libros digitales, inspirado en [Komga](https://komga.org/),
pensado para funcionar bien en hardware modesto.

## El nombre

**Diar** viene de mi alias, *diarmund*. **Speicher** es "almacén" en alemán.
Un almacén de libros, con mi nombre encima.

## Por qué existe

Komga es excelente, pero está pensado para servidores con margen de sobra. Yo quería
servir mi biblioteca desde una **TV box de 4 GB de RAM y 6 núcleos**, un equipo que se
queda corto en cuanto algo asume que la memoria es barata.

Esa restricción es el criterio de diseño de todo el proyecto, no una nota al pie:

- **Escaneo incremental por `mtime`**: no se recorre la biblioteca entera en cada arranque,
  solo los directorios que cambiaron.
- **Streaming de páginas bajo demanda**: nunca se descomprime un cómic completo en memoria;
  se extrae la página pedida y nada más.
- **Caché de páginas en disco con expulsión LRU**: el trabajo de descompresión se paga una
  vez, y la caché tiene techo para que no se coma el almacenamiento.
- **Hashing por muestreo**: identificar un fichero de 500 MB no requiere leerlo entero.
- **SQLite en modo WAL**: sin servidor de base de datos aparte comiéndose la RAM.

## Por qué .NET

Porque **C# es mi lenguaje favorito**, sin más vueltas.

Que además .NET 10 rinda muy bien y consuma poco en ARM fue lo que confirmó que la
decisión era sensata, pero el orden real fue ese: primero el gusto, después la
justificación técnica.

## Qué habla

La idea es que los lectores que ya usas funcionen sin adaptadores:

| Protocolo | Ruta | Para qué |
|---|---|---|
| OPDS v1.2 | `/opds/v1.2/...` | Lectores clásicos (Chunky, Panels, Moon+) |
| OPDS v2.0 | `/opds/v2.0/...` | Readium JSON-LD / WebPub |
| API Komga | `/api/v1/...` | Clientes que ya hablan Komga (CDisplayEx, Komelia) |
| KOReader Sync | `/koreader/{apiKey}/...` | Sincronización de progreso |
| Kobo Sync | `/kobo/{apiKey}/...` | Dispositivos Kobo nativos |
| API v2 | `/api/v2/...` | API nativa del proyecto |

Formatos: `.cbz`, `.cbr`, `.zip`, `.rar`, `.epub` y `.pdf`.

De los PDF se cuentan las páginas y se extraen las imágenes embebidas. Un PDF de texto y
fuentes no genera portada: el procesador no rasteriza.

## Stack

- **.NET 10** / C# 13
- **EF Core 10** sobre SQLite (WAL)
- **SharpCompress** para RAR/CBR
- `System.IO.Compression` para ZIP/CBZ y EPUB
- **SkiaSharp** para miniaturas, **PdfPig** para PDF; EPUB con `System.Xml.Linq` sobre el `.opf`
- **HotChocolate** para GraphQL
- Minimal APIs, sin MVC

Tres proyectos: `Core` (dominio y utilidades sin dependencias de infraestructura),
`Infrastructure` (persistencia, escáner, procesadores) y `Api` (endpoints y middleware).

## Desplegar

```sh
docker compose up -d --build
```

El contenedor **no publica su puerto**: se une a la red externa del Gateway y solo es
alcanzable desde ella. Esa es la barrera que sostiene la confianza en las cabeceras de
identidad, así que no la abras.

`.env` en la raíz:

| Variable           | Uso                                                              |
| ------------------ | ---------------------------------------------------------------- |
| `PATH_BASE`        | Prefijo bajo el que el Gateway lo monta, sin barras. Vacío = raíz |
| `NETWORK_NAME`     | Red Docker externa compartida con el Gateway                      |
| `STORAGE_PATH`     | Ruta del host para base de datos, miniaturas y caché              |
| `LIBRARIES_PATH`   | Ruta del host con los cómics. Escribible si `ENABLE_UPLOAD=true`  |
| `PAGE_CACHE_BYTES` | Techo de la caché de páginas en disco                             |
| `ENABLE_UPLOAD`    | Subida de ficheros desde la API                                   |

La imagen usa Debian slim en lugar de distroless porque `libSkiaSharp.so` enlaza contra
fontconfig y freetype, y .NET exige `libicu`: el orden natural de páginas usa
`CompareOptions.NumericOrdering`, que depende de ICU y se rompería con el modo invariante.

## Arrancar en local

```bash
git clone <repo> && cd DiarSpeicher
dotnet run --project src/DiarSpeicher.Api
```

Para acceder desde otro dispositivo de la red hay que escuchar en todas las interfaces,
no solo en loopback:

```bash
dotnet run --project src/DiarSpeicher.Api --urls http://0.0.0.0:5000
```

Tests:

```bash
dotnet test tests/DiarSpeicher.Tests/DiarSpeicher.Tests.csproj
```

## Configuración

En `appsettings.json`:

```jsonc
"Storage": {
  "RootPath": "storage",           // miniaturas y caché
  "PageCache": {
    "Enabled": true,
    "MaxBytes": 2147483648,         // 2 GiB de techo
    "MaxEntryBytes": 33554432,      // páginas mayores no se cachean
    "EvictionTargetRatio": 0.9      // al expulsar, baja al 90%
  },
  "Upload": {
    "MaxRequestBytes": 1073741824
  }
}
```

## Estado

El núcleo está cerrado: base de datos, escáner, extracción, catálogos OPDS y
compatibilidad con clientes. 118 pruebas en verde.

El backend **no autentica**: se sirve tras el Gateway
[tvboxHealth](../Gateway/README.md), que resuelve la identidad y se la pasa en cabeceras.
Aquí solo quedan los medios.

- [Índice de documentación](documentation/INDEX.md)
- [Documentación del backend](documentation/backend/README.md)
- [Pendiente](documentation/PENDIENTE.md)

Limitaciones conocidas:

- Las miniaturas de **JXL** no se generan: SkiaSharp no decodifica el formato y se sirve
  el original sin redimensionar.
- Un **PDF de solo texto** cuenta páginas pero no produce portada.

## Créditos

Inspirado en [Komga](https://github.com/gotson/komga) y en [Stump](https://github.com/stumpapp/stump),
de donde vienen buena parte de los algoritmos de escaneo y hashing.
