# Subida de Archivos y Gestión de Cargas (Uploads)

## 1. Visión General

DiarSpeicher implementa un subsistema de subida de archivos que permite a los usuarios con permisos adecuados incorporar libros digitales y series completas directamente al almacenamiento del servidor a través de la API REST o interfaz web, sin necesidad de acceso directo por SSH/SMB al sistema de archivos del host.

Tras completarse cualquier subida exitosa, el sistema dispara inmediatamente una tarea en segundo plano hacia `IScannerQueue` para indexar los nuevos contenidos de forma no bloqueante.

---

## 2. Configuración y Variables de Entorno

Por seguridad, la subida de archivos está protegida por directivas de configuración:

| Variable de Configuración | Variable de Entorno | Valor por Defecto | Descripción |
|---|---|---|---|
| `EnableUpload` | `DIAR_ENABLE_UPLOAD` | `true` | Habilita o deshabilita globalmente los endpoints de carga. Si es `false`, los endpoints retornan HTTP 404/403. |
| `MaxFileUploadSize` | `DIAR_MAX_FILE_UPLOAD_SIZE` | `524288000` (500 MB) | Tamaño máximo en bytes por archivo individual. |
| `AllowedExtensions` | `DIAR_ALLOWED_EXTENSIONS` | `.cbz,.cbr,.epub,.pdf,.zip` | Lista blanca de extensiones permitidas para importación. |

---

## 3. Especificación de Endpoints REST

### 1. Subida de Libros Individuales (`POST /api/v2/libraries/{id}/upload`)

Permite subir uno o varios archivos de libros (`.cbz`, `.cbr`, `.epub`, `.pdf`) a una biblioteca específica.

- **Método**: `POST`
- **Ruta**: `/api/v2/libraries/{id}/upload`
- **Content-Type**: `multipart/form-data`
- **Cabeceras**: `Authorization: Bearer <apiKey>` o `X-API-Key: <apiKey>`
- **Parámetros Opcionales de Formulario**:
  - `subpath`: Subcarpeta relativa dentro de la biblioteca donde se ubicarán los archivos (ej. `Manga/Berserk`). Si no se especifica, se colocan en la raíz de la biblioteca.
  - `files`: Colección de archivos binarios enviados en el formulario multipart.

#### Ejemplo de Petición (`curl`):
```bash
curl -X POST "http://localhost:5000/api/v2/libraries/lib_123/upload" \
  -H "X-API-Key: mi_api_key" \
  -F "subpath=Batman" \
  -F "files=@Batman_01.cbz" \
  -F "files=@Batman_02.cbz"
```

#### Respuesta Exitosa (`200 OK`):
```json
{
  "uploadedCount": 2,
  "files": [
    {
      "name": "Batman_01.cbz",
      "path": "/storage/comics/Batman/Batman_01.cbz",
      "size": 34852910
    },
    {
      "name": "Batman_02.cbz",
      "path": "/storage/comics/Batman/Batman_02.cbz",
      "size": 36192044
    }
  ],
  "scanJobTriggered": true
}
```

---

## 4. Medidas de Seguridad y Sanitización

Para prevenir vulnerabilidades críticas (CWE-22 Path Traversal, CWE-434 Unrestricted File Upload):

1. **Prevención de Path Traversal**:
   - Se sanitiza el parámetro `subpath` y el nombre del archivo (`Path.GetFileName`).
   - Se rechazan terminantemente secuencias con `..`, caracteres nulos (`\0`) y barras invertidas descontroladas.
   - Se resuelve la ruta canónica absoluta (`Path.GetFullPath`) y se valida que empiece estrictamente por el prefijo del directorio raíz de la biblioteca:
     ```csharp
     if (!targetFullPath.StartsWith(library.Path, StringComparison.OrdinalIgnoreCase))
     {
         return Results.BadRequest(new { error = "Intento de acceso fuera del directorio de la biblioteca." });
     }
     ```
2. **Protección contra Sobrescritura Accidental**:
   - Si el archivo de destino ya existe, el servidor puede rechazarlo (`409 Conflict`) o generar un sufijo único (`Batman_01 (1).cbz`) según el parámetro `overwrite=false`.
3. **Validación de Tipo y Firma Mágica**:
   - Además de verificar la extensión, se validan los primeros bytes (números mágicos): `PK\x03\x04` para ZIP/CBZ/EPUB, `Rar!\x1A\x07` para RAR/CBR.

---

## 5. Integración con el Motor de Escaneo Reactivo

Al culminar la escritura del archivo en disco:
1. El endpoint inyecta `IScannerQueue`.
2. Se encola la solicitud de escaneo:
   ```csharp
   await scannerQueue.QueueScanAsync(new ScanRequest(libraryId), ct);
   ```
3. El `ScanBackgroundService` recoge el trabajo y ejecuta el `DirectoryScanner` sobre la carpeta afectada, detectando la nueva serie y libro en milisegundos mediante la caché de `mtime`.
