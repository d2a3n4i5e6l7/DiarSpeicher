# Fase 1 · Gestión de bibliotecas

## Punto de partida

La única forma de crear una biblioteca hoy es el diálogo embebido en `UploadPage.tsx`, que
envía `name`, `path` y `description`. No hay manera de lanzar un escaneo desde la interfaz
ni de ver el resultado del último.

## Ampliación de la API

`StumpLibraryDto` solo serializaba `id`, `name`, `path`, `status` y `seriesCount`, y
`CreateLibraryAsync` fijaba `LibraryPattern.SeriesBased` ignorando el resto de la
`LibraryConfig`. Ambas cosas se han corregido en esta fase.

`StumpLibraryDto` añade `mediaCount`, `description`, `emoji`, `createdAt`, `updatedAt`,
`lastScannedAt` y un objeto `config` con las quince opciones de `LibraryConfig`. Los enums
viajan como el nombre del miembro (`"Manga"`, `"RightToLeft"`), no como su índice, para que
el contrato no dependa del orden de declaración.

| Endpoint                           | Estado                                            |
| ---------------------------------- | ------------------------------------------------- |
| `GET /api/v2/libraries`            | DTO ampliado, con `config`                         |
| `GET /api/v2/libraries/{id}`       | DTO ampliado, con `config`                         |
| `POST /api/v2/libraries`           | Acepta `emoji` y `config` completa                 |
| `PUT /api/v2/libraries/{id}`       | Nuevo. Campo ausente significa "no tocar"          |
| `DELETE /api/v2/libraries/{id}`    | Nuevo. Borra el índice, nunca los ficheros         |
| `POST /api/v2/libraries/{id}/scan` | Sin cambios                                        |

`StumpLibraryDto` es exclusivo de `/api/v2`: los clientes OPDS y Komga consumen
`KomgaLibraryDto` y los feeds XML, que no se han tocado. Los 137 tests de contrato siguen
pasando.

La ruta no es editable: cambiarla dejaría el índice apuntando a ficheros que ya no están,
y el camino correcto para mover una biblioteca es crear otra y reescanear.

## Trabajo

**Extraer la gestión de bibliotecas a su propia sección.** El diálogo de alta sale de
`UploadPage.tsx` y pasa a `pages/LibrariesPage.tsx`; la subida se queda solo con el selector
de biblioteca que ya tiene. Nueva entrada `/libraries` en `navItems.ts`, sobre "Subida de
ficheros", con su icono en el mapa `ICONS` de `AppLayout.tsx`.

**Listado como tarjetas**, no como tabla: nombre, ruta, estado, series, tomos, tipo de
contenido, descripción y fecha del último escaneo. Estado vacío con llamada a la acción
cuando no hay ninguna.

**Alta y edición** en un mismo diálogo, con la configuración avanzada en un acordeón
plegado: quien no lo abra obtiene los valores por defecto del backend. El formulario de
configuración vive en `components/LibraryConfigForm.tsx` para no inflar la página.

**Borrado** con confirmación que deja claro que los ficheros del disco no se tocan.

**Lanzar escaneo** por tarjeta con `librariesApi.scan(id)`. El botón se deshabilita mientras
el estado sea `SCANNING`.

**Progreso del escaneo.** El backend lo publica por suscripción GraphQL
(`Subscriptions.JobProgress`), y la SPA no tiene cliente GraphQL. Esta fase resuelve con
sondeo de `librariesApi.list()` cada pocos segundos mientras alguna biblioteca esté
escaneando, y lo detiene al terminar. El cliente de suscripciones queda como deuda.

## Ampliación del cliente

`librariesApi` gana `get(id)`, `scan(id)`, `update(id, payload)` y `delete(id)`.
`LibraryItem` refleja el DTO ampliado, y `LibraryConfig` con `DEFAULT_LIBRARY_CONFIG`
espeja los valores por defecto de la entidad.

## Hecho cuando

Las bibliotecas tienen sección propia donde se crean eligiendo tipo, patrón y opciones de
escaneo, se editan, se borran del índice y se escanean; el estado se refresca solo mientras
dura el escaneo, y `UploadPage` ya no contiene lógica de creación.
