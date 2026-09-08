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

Formatos: `.cbz`, `.cbr`, `.zip`, `.rar` y `.epub`.
El soporte de `.pdf` está pendiente — hoy se acepta pero no se procesa.

## Stack

- **.NET 10** / C# 13
- **EF Core 10** sobre SQLite (WAL)
- **SharpCompress** para RAR/CBR
- `System.IO.Compression` para ZIP/CBZ y EPUB
- Minimal APIs, sin MVC

Tres proyectos: `Core` (dominio y utilidades sin dependencias de infraestructura),
`Infrastructure` (persistencia, escáner, procesadores) y `Api` (endpoints y middleware).

## Arrancar

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

En desarrollo activo. La **Fase 1** (el núcleo: base de datos, escáner, extracción y
compatibilidad con clientes) está en su mayor parte funcionando, con huecos identificados
y planificados.

- [Índice de documentación](documentation/INDEX.md)
- [Plan de cierre de la Fase 1](documentation/PLAN_CIERRE_FASE_1.md) — auditoría del
  estado real y trabajo pendiente

> ⚠️ **Todavía no lo expongas fuera de tu red.** La autenticación está sin terminar:
> hoy las contraseñas no se validan. Está identificado y planificado, pero conviene
> saberlo antes de abrir un puerto.

## Créditos

Inspirado en [Komga](https://github.com/gotson/komga) y en [Stump](https://github.com/stumpapp/stump),
de donde vienen buena parte de los algoritmos de escaneo y hashing.
