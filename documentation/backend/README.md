# Documentación del backend de DiarSpeicher

`DiarSpeicher` es un **servidor de cómics y libros digitales** en .NET 10, pensado para
correr en hardware modesto (una TV box de 4 GB de RAM y 6 núcleos). Indexa bibliotecas del
sistema de ficheros con un escaneo incremental por `mtime`, extrae páginas bajo demanda sin
descomprimir el archivo completo, y expone el catálogo por seis protocolos distintos —OPDS
v1.2, OPDS v2.0, la API de Komga, la API v2 nativa, KOReader Sync y Kobo Sync— más un
servidor GraphQL.

La persistencia es una única base **SQLite en modo WAL** gestionada con EF Core 10. No hay
servidor de base de datos aparte, ni caché en memoria distribuida, ni ningún proceso extra:
un solo binario y un fichero `.db`.

## Índice

| Documento                                                | Contenido                                                                     |
| -------------------------------------------------------- | ------------------------------------------------------------------------------ |
| [01-arquitectura.md](01-arquitectura.md)                 | Capas Api/Core/Infrastructure, `Program.cs`, flujo de una petición, paquetes  |
| [02-modelo-de-datos.md](02-modelo-de-datos.md)           | Las 17 entidades, el mapeo de `DiarSpeicherDbContext` y las relaciones        |
| [03-escaneo-y-medios.md](03-escaneo-y-medios.md)         | Escáner con caché de mtimes, watcher, procesadores, orden natural, portadas   |
| [04-apis-y-protocolos.md](04-apis-y-protocolos.md)       | Inventario de endpoints por módulo: OPDS, Komga, v2, KOReader, Kobo, GraphQL  |
| [05-autenticacion-actual.md](05-autenticacion-actual.md) | Resolución de identidad desde cabeceras y espejo de usuario |

## Convención de rutas

Cada protocolo cuelga de su propio prefijo y se registra en `Program.cs` con un
`Map*Endpoints()` por módulo:

| Prefijo                | Registro                                                                 |
| ---------------------- | ------------------------------------------------------------------------ |
| `/opds/v1.2`           | [OpdsEndpoints.cs](../../src/DiarSpeicher.Api/Endpoints/OpdsEndpoints.cs)       |
| `/opds/v2.0`           | [OpdsV2Endpoints.cs](../../src/DiarSpeicher.Api/Endpoints/OpdsV2Endpoints.cs)   |
| `/api/v1`              | [KomgaEndpoints.cs](../../src/DiarSpeicher.Api/Endpoints/KomgaEndpoints.cs)     |
| `/api/v2`              | [StumpV2Endpoints.cs](../../src/DiarSpeicher.Api/Endpoints/StumpV2Endpoints.cs)  |
| `/koreader/{apiKey}`   | [KoReaderEndpoints.cs](../../src/DiarSpeicher.Api/Endpoints/KoReaderEndpoints.cs) |
| `/kobo/{apiKey}`       | [KoboEndpoints.cs](../../src/DiarSpeicher.Api/Endpoints/KoboEndpoints.cs)       |
| `/graphql`             | `MapGraphQL()` en [Program.cs](../../src/DiarSpeicher.Api/Program.cs)           |

Las rutas OPDS admiten además una variante con la clave incrustada
(`/opds/{apiKey}/v1.2/...`), porque hay lectores que no saben mandar cabeceras.

## Estado

Fase 1 (núcleo: base de datos, escáner, extracción y compatibilidad con clientes) en su
mayor parte funcionando. 128 tests pasando en
[tests/DiarSpeicher.Tests](../../tests/DiarSpeicher.Tests).

Las limitaciones conocidas —JXL sin redimensionar, PDF de solo texto sin portada— están
documentadas en [03-escaneo-y-medios.md](03-escaneo-y-medios.md). El estado de la
autenticación y su sustitución prevista, en
[05-autenticacion-actual.md](05-autenticacion-actual.md).
