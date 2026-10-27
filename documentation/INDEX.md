# Documentación · DiarSpeicher

Backend de medios en .NET 11: escaneo de bibliotecas, streaming de cómics y EPUB, y
catálogos OPDS sobre SQLite. Se despliega como contenedor propio y se sirve al exterior a
través del Gateway [tvboxHealth](../../Gateway/README.md), que resuelve la identidad y le
pasa el usuario ya autenticado en cabeceras.

## Backend

| Documento                                                        | Contenido                                                          |
| ---------------------------------------------------------------- | ------------------------------------------------------------------ |
| [01-arquitectura.md](backend/01-arquitectura.md)                 | Capas, arranque, flujo de una petición, paquetes                   |
| [02-modelo-de-datos.md](backend/02-modelo-de-datos.md)           | Entidades, relaciones y mapeo con EF Core sobre SQLite             |
| [03-escaneo-y-medios.md](backend/03-escaneo-y-medios.md)         | Caché de mtimes, procesadores por formato, orden natural, portadas |
| [04-apis-y-protocolos.md](backend/04-apis-y-protocolos.md)       | OPDS v1.2 y v2, API Komga, API v2 nativa, KOReader, Kobo           |
| [05-autenticacion-actual.md](backend/05-autenticacion-actual.md) | Resolución de identidad desde cabeceras y espejo de usuario        |

El índice propio del backend está en [backend/README.md](backend/README.md).

## Frontend

Cliente Blazor WebAssembly + MudBlazor en `src/DiarSpeicher.Client`. El plan de la migración está en
[plan-migracion-blazor.md](plan-migracion-blazor.md).
