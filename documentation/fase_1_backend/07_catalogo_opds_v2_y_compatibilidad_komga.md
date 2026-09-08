# Catálogo OPDS v2.0 (Readium JSON-LD) y API REST de Compatibilidad Komga

## 1. Visión General

DiarSpeicher implementa dos capas de interoperabilidad modernas además de OPDS v1.2:
1. **OPDS v2.0 (Readium WebPub / JSON-LD)**: Para clientes de lectura modernos compatibles con la especificación Readium OPDS 2.0 (`application/opds+json`, `application/opds-publication+json`, `application/opds-authentication+json`).
2. **API REST Komga (`/api/v1/`)**: Para compatibilidad nativa con lectores y aplicaciones del ecosistema Komga (como Panels, Paperback, Tachi/Mihon con extensión Komga, CDisplayEx, etc.).

---

## 2. Especificación OPDS v2.0

### MIME Types Soportados
- `application/opds+json`: Catálogos, feeds de navegación y agrupaciones de publicaciones.
- `application/opds-publication+json`: Manifiesto de publicación individual (WebPub Manifest).
- `application/opds-authentication+json`: Documento de autenticación.
- `application/vnd.readium.progression+json`: Formato para sincronización de progreso de lectura.

### Rutas de Endpoints
Todas las rutas están disponibles tanto con API Key en la URL como con Basic Auth:
- `/opds/v2.0/catalog` y `/opds/{apiKey}/v2.0/catalog`: Catálogo raíz con navegación facetada (`libraries`, `series`, `books/browse`, `books/keep-reading`, `search`).
- `/opds/v2.0/libraries` y `/opds/{apiKey}/v2.0/libraries`: Bibliotecas disponibles para el usuario autenticado.
- `/opds/v2.0/libraries/{id}` y `/opds/{apiKey}/v2.0/libraries/{id}`: Series dentro de una biblioteca específica.
- `/opds/v2.0/series/{id}` y `/opds/{apiKey}/v2.0/series/{id}`: Publicaciones (`readingOrder`) dentro de una serie.
- `/opds/v2.0/books/browse` y `/opds/{apiKey}/v2.0/books/browse`: Feed paginado de todos los libros permitidos.
- `/opds/v2.0/books/keep-reading` y `/opds/{apiKey}/v2.0/books/keep-reading`: Libros actualmente en progreso (`ReadingStatus.Reading`).
- `/opds/v2.0/books/{id}` y `/opds/{apiKey}/v2.0/books/{id}`: Manifiesto de publicación individual (`OpdsV2Publication`) con `readingOrder` página por página y link de adquisición streaming.
- `/opds/v2.0/books/{id}/progression` (GET/PUT): Consulta y actualización del progreso de lectura (`locator` con `href`, `locations.position` o `locations.progression`).
- `/opds/v2.0/auth`: Documento de autenticación OPDS 2.0.

---

## 3. Especificación API Komga (`/api/v1/`)

La API REST compatible con Komga emula la estructura de modelos y paginación de Spring Data:

### Formato Paginado (`KomgaPageResponse<T>`)
```json
{
  "content": [ ... ],
  "pageable": {
    "pageNumber": 0,
    "pageSize": 20,
    "offset": 0
  },
  "totalElements": 42,
  "totalPages": 3,
  "last": false,
  "first": true,
  "numberOfElements": 20,
  "size": 20,
  "number": 0,
  "empty": false
}
```

### Endpoints Implementados
- `GET /api/v1/libraries`: Lista de bibliotecas no excluidas para el usuario.
- `GET /api/v1/libraries/{id}`: Detalle de biblioteca.
- `GET /api/v1/series`: Lista paginada de series (filtro opcional por `library_id`).
- `GET /api/v1/series/{id}`: Detalle de serie.
- `GET /api/v1/series/{id}/books`: Libros de una serie con paginación Spring.
- `GET /api/v1/books`: Lista paginada de libros (con soporte de búsqueda por término `search`).
- `GET /api/v1/books/latest`: Libros agregados recientemente.
- `GET /api/v1/books/{id}`: Detalle de libro (`KomgaBookDto`) con metadatos completos y número de páginas.
- `GET /api/v1/books/{id}/pages`: Lista de páginas del libro (`number`, `fileName`, `mediaType`, `size`).
- `GET /api/v1/books/{id}/pages/{pageNumber}`: Extracción y streaming de imagen de la página con cabeceras de caché.
- `GET /api/v1/books/{id}/thumbnail`: Streaming de la miniatura de portada.
- `PATCH /api/v1/books/{id}/read-progress`: Actualización de página actual y estado de lectura (`completed`).
- `DELETE /api/v1/books/{id}/read-progress`: Reinicio de progreso de lectura.

---

## 4. Control de Acceso y Filtrado Parental

Tanto el motor OPDS v2.0 como el adaptador Komga ejecutan **exclusivamente** consultas filtradas con la extensión de dominio:
```csharp
var query = _db.Media.ForUser(user);
var seriesQuery = _db.Series.ForUser(user);
var libraryQuery = _db.Libraries.ForUser(user);
```
- Se respetan bibliotecas explícitamente excluidas (`LibraryExclusion`).
- Se evalúan restricciones de edad (`AgeRestriction.Age`), garantizando que usuarios menores no puedan listar, descargar ni ver miniaturas de contenidos restringidos ni en feeds ni en endpoints directos.
