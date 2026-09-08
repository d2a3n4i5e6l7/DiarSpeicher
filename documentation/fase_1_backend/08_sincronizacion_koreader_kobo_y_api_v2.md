# Sincronización KOReader, Kobo Sync Server y API REST v2 de Stump

## 1. Visión General

DiarSpeicher integra tres motores clave para e-readers dedicados y clientes modernos de Stump:
1. **KOReader Sync Protocol (`/koreader/{apiKey}/...`)**: Servidor de sincronización de progreso de lectura compatible con el plugin nativo "Kosync" de KOReader.
2. **Kobo Sync Server (`/kobo/{apiKey}/...`)**: Servidor de sincronización nativo para e-readers Kobo, permitiendo que dispositivos Kobo descubran, sincronicen metadatos y descarguen libros EPUB directamente a través de Wi-Fi.
3. **API REST v2 Nativa de Stump (`/api/v2/...`)**: Implementación moderna del contrato REST v2 de Stump para clientes web/desktop, incluyendo soporte de paginación basada en cursor/página, streaming de medios, disparador de escaneo en background y motor de extracción web para EPUBs.

---

## 2. Protocolo de Sincronización KOReader (Kosync)

KOReader utiliza un protocolo HTTP simple con JSON para almacenar y recuperar la posición de lectura en documentos sincronizados mediante un hash característico (`koreader_hash`).

### Identificación de Documentos
- KOReader calcula un hash MD5 muestreando fragmentos de tamaño exponencial del archivo: $1024 \times 2^i$.
- DiarSpeicher computa y almacena este valor en `Media.KoReaderHash` durante el escaneo de medios.
- Al consultar o reportar progreso, el cliente envía `document` (el hash MD5 o alternativamente el identificador del libro).

### Endpoints Implementados
- `GET /koreader/{apiKey}/users/auth` / `GET /koreader/{apiKey}/users/create`: Verificación de credenciales (retorna `{"authorized": "OK"}`).
- `GET /koreader/{apiKey}/syncs/progress/{document}`: Obtiene el último progreso registrado para el documento.
- `PUT /koreader/{apiKey}/syncs/progress`: Actualiza la posición de lectura.
  - Payload:
    ```json
    {
      "document": "e2fc714c4727ee9395f324cd2e7f331f",
      "progress": "epubcfi(/6/4[chap01]!/4/2/10/1:0)",
      "percentage": 0.45,
      "device": "Kobo Clara 2E",
      "device_id": "kobo-clara-01",
      "timestamp": 1725750000
    }
    ```
  - Efecto: Actualiza o crea la `ReadingSession` asociada al usuario y libro, marcando el estado en `Reading` o `Completed` (si `percentage >= 1.0`).

---

## 3. Servidor de Sincronización Kobo (Kobo Sync Protocol)

El protocolo de sincronización de Kobo permite sincronizar la biblioteca de libros con dispositivos Kobo sin necesidad de cables ni Calibre.

### Flujo de Sincronización
1. **Inicialización (`GET /kobo/{apiKey}/v1/initialization`)**:
   - Devuelve las URLs de configuración del dispositivo (`Resources`, `ImageHost`, `SyncHost`).
2. **Sincronización de Biblioteca (`GET /kobo/{apiKey}/v1/library/sync`)**:
   - Envía los libros asignados al usuario en formato `KoboBookEntitlementContainer`.
   - Soporta tokens de sincronización incremental mediante la cabecera `x-kobo-synctoken`.
   - **Exclusión por formato**: Únicamente los libros en formato EPUB (`application/epub+zip`) son enviados a los dispositivos Kobo.
   - **Restricción parental**: Aplica estrictamente `ForUser(user)`.
3. **Metadatos (`GET /kobo/{apiKey}/v1/library/{bookId}/metadata`)**:
   - Devuelve la ficha completa del libro (`KoboBookMetadata`), autor, serie, número de secuencia y fecha de publicación.
4. **Descarga de EPUB (`GET /kobo/{apiKey}/v1/books/{bookId}/file/epub`)**:
   - Descarga directa del archivo binario del libro para su lectura en el dispositivo.
5. **Miniaturas (`GET /kobo/{apiKey}/v1/books/{bookId}/thumbnail/{width}/{height}/{isGreyscale}/image.jpg`)**:
   - Variante con calidad: `.../thumbnail/{width}/{height}/{quality}/{isGreyscale}/image.jpg`.
   - Streaming de la portada generada en caché, siempre en JPEG: el dispositivo Kobo no
     admite otros formatos.
   - El dispositivo no construye estas URLs por su cuenta: las recibe en `image_url_template`
     e `image_url_quality_template` dentro de la respuesta de `/v1/initialization`.

> **Decisión (bloque B2.2 del plan de cierre, 8 de septiembre de 2026).** Versiones
> anteriores de este documento describían `/v1/library/{bookId}/file/{revisionId}` y
> `/v1/images/{bookId}/cover.jpg`. Son incorrectas: el router de referencia de Stump
> (`stump/apps/server/src/routers/kobo/router.rs`) registra exactamente las rutas que
> implementa DiarSpeicher, y son las que el dispositivo recibe a través de las plantillas de
> `/v1/initialization`. Se corrigió el documento y **no** el código.

---

## 4. API REST v2 Nativa de Stump (`/api/v2/`)

La API REST v2 reproduce fielmente el contrato de Stump v2 para clientes modernos:

### Paginación Estándar (`StumpPageResponse<T>`)
```json
{
  "data": [ ... ],
  "_page": {
    "total_pages": 4,
    "current_page": 0,
    "page_size": 20,
    "total_elements": 75
  }
}
```

### Endpoints de Medios y Lectura
- `GET /api/v2/media`: Lista paginada de medios accesibles con soporte para parámetro `search`.
- `GET /api/v2/media/keep-reading`: Lista de medios en progreso (`ReadingStatus.Reading`).
- `GET /api/v2/media/{id}`: Detalle de libro (`StumpMediaDto`) con metadatos completos y recuento de páginas.
- `GET /api/v2/media/{id}/page/{page}`: Streaming de la página solicitada (CBZ/CBR/EPUB).
- `GET /api/v2/media/{id}/thumbnail`: Streaming de la carátula o miniatura en caché.
- `GET /api/v2/media/{id}/download`: Descarga del archivo original del libro.
- `PUT /api/v2/media/{id}/progress`: Actualización de página y estado de lectura (`is_complete`).

### Endpoints de Bibliotecas y Series
- `GET /api/v2/libraries`: Lista paginada de bibliotecas permitidas.
- `GET /api/v2/libraries/{id}`: Detalle de biblioteca.
- `POST /api/v2/libraries/{id}/scan`: Encolamiento reactivo en background de un escaneo de biblioteca mediante `IScannerQueue`.
- `GET /api/v2/series`: Lista paginada de series (filtro opcional `library_id`).
- `GET /api/v2/series/{id}`: Detalle de serie.
- `GET /api/v2/series/{id}/media`: Libros dentro de una serie.

### Motor de Extracción Web para EPUB (`/api/v2/epub/`)
Permite a clientes web renderizar libros EPUB sin necesidad de descargar el archivo completo:
- `GET /api/v2/epub/{id}/toc`: Extrae y normaliza la tabla de contenidos (TOC) desde archivos `toc.ncx` o XHTML/HTML del EPUB.
- `GET /api/v2/epub/{id}/resource/{*resourcePath}`: Extrae y transmite cualquier recurso interno del EPUB (CSS, imágenes, fuentes, capítulos XHTML) resolviendo rutas relativas y asignando el Content-Type adecuado.

### Monitoreo del Sistema
- `GET /api/v2/system/status`: Devuelve el estado operativo de DiarSpeicher (`{"status": "healthy", "version": "1.0.0"}`).

---

## 5. Control Perimetral y Seguridad

Todos los endpoints de KOReader, Kobo y Stump v2:
1. Son autenticados de manera uniforme por `OpdsAuthMiddleware` mediante token en URL (`/{service}/{apiKey}/...`), cabecera `Authorization: Bearer <apiKey>` o cabecera `x-api-key`.
2. Filtran absolutamente todas las consultas mediante `_db.Media.ForUser(user)`, `_db.Series.ForUser(user)` y `_db.Libraries.ForUser(user)`.
3. Verifican los permisos de biblioteca y restricciones de edad (`AgeRestriction`), asegurando aislamiento multicolección y control parental en todas las interfaces de lectura.
