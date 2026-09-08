# Documentación · DiarSpeicher

Backend de medios en .NET 10: escaneo de bibliotecas, streaming de cómics y EPUB, y
catálogos OPDS sobre SQLite. Se despliega como contenedor propio y se sirve al exterior a
través del Gateway [tvboxHealth](../../Gateway/README.md), que resuelve la identidad y le
pasa el usuario ya autenticado en cabeceras.

## Backend

| Documento                                                        | Contenido                                                          |
| ---------------------------------------------------------------- | ------------------------------------------------------------------ |
| [01-arquitectura.md](backend/01-arquitectura.md)                 | Capas, arranque, flujo de una petición, paquetes                    |
| [02-modelo-de-datos.md](backend/02-modelo-de-datos.md)           | Entidades, relaciones y mapeo con EF Core sobre SQLite              |
| [03-escaneo-y-medios.md](backend/03-escaneo-y-medios.md)         | Caché de mtimes, procesadores por formato, orden natural, portadas  |
| [04-apis-y-protocolos.md](backend/04-apis-y-protocolos.md)       | OPDS v1.2 y v2, API Komga, API v2 nativa, KOReader, Kobo, GraphQL   |
| [05-autenticacion-actual.md](backend/05-autenticacion-actual.md) | Resolución de identidad desde cabeceras y espejo de usuario         |

El índice propio del backend está en [backend/README.md](backend/README.md).

## Pendiente

[PENDIENTE.md](PENDIENTE.md) recoge lo que falta, verificado contra el código: en el backend
los enlaces de los feeds bajo un prefijo, las miniaturas JXL y las portadas de PDF de solo
texto; en el Gateway la propagación de las cabeceras de identidad, la verificación DPoP y
los supuestos que impiden montar varios dominios.

## Referencia externa

El proyecto toma como referencia funcional a [Stump](https://github.com/stumpapp/stump),
cuyo código fuente está bajo `stump/` para consulta. La correspondencia no es literal: el
backend reimplementa el comportamiento en .NET, no traduce el Rust.
