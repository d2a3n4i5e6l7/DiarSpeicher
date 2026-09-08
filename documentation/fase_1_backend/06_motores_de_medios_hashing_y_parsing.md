# Análisis Detallado de Motores de Medios y Parsing: Rust -> .NET 10

Este documento complementa la documentación técnica con la disección interna de los módulos de extracción física de archivos, algoritmos criptográficos y parsing de metadatos de Stump.

---

## 1. Mapeo de Procesadores por Formato (`stump/core/src/filesystem/media/format/`)

Stump implementa un trait `FileProcessor` con implementaciones específicas para cada tipo de archivo:

| Formato | Implementación en Rust | Equivalente en .NET 10 | Consideraciones Críticas |
|---|---|---|---|
| **ZIP / CBZ** | `format/zip.rs` (`zip-rs`) | `System.IO.Compression.ZipArchive` | Extrae páginas en streaming directamente a memoria; orden alfanumérico estricto (`sort_file_names`) ignorando ficheros ocultos (`.DS_Store`, `__MACOSX`). |
| **RAR / CBR** | `format/rar.rs` (`unrar`) | `SharpCompress.Archives.Rar` | Lectura de headers sin extraer todo a disco. Soporte de conversion opcional RAR -> ZIP si está configurado en `LibraryConfig`. |
| **EPUB** | `format/epub.rs` (`epub-rs`, `quick-xml`) | `VersOne.Epub` + `System.Xml.Linq` | Localiza el archivo `.opf`, extrae metadatos Dublin Core (autores, serie, fecha, resumen) y la imagen de portada marcada con `id="cover"` o en el manifiesto. |
| **PDF** | `format/pdf.rs` (`pdf-rs` / `poppler`) | `PdfPig` o `SkiaSharp` / `PDFtoImage` | Renderizado de primera página para portada y conteo de páginas. |

---

## 2. Algoritmos de Hashing

Stump calcula dos hashes independientes para cada libro:

### 2.1 Hash Interno de Stump (`hash.rs`)
- **Propósito**: Detectar libros duplicados sin que el hash cambie cuando se alteran los metadatos o tags del archivo.
- **Algoritmo**:
  - Muestra: `HASH_SAMPLE_SIZE = 10000` (10 KB) repetido `HASH_SAMPLE_COUNT = 4` veces.
  - Si el archivo mide $\le 40$ KB, lee todo el archivo y calcula `SHA-256`.
  - Si el archivo mide $> 40$ KB, toma 4 bloques distribuidos equitativamente por el archivo:
    - Offset $i = (size / 4) \times i$ para $i \in [0, 3]$.
    - Un bloque final en $(size - 10000)$.
    - Los acumula en un digest **SHA-256** y los formatea en hexadecimal en minúsculas.

### 2.2 Hash de Sincronización KOReader (`generate_koreader_hash`)
- **Propósito**: Compatibilidad idéntica con el algoritmo que KOReader (escrito en Lua) genera en los dispositivos e-ink para sincronizar páginas leídas.
- **Algoritmo**:
  - Utiliza **MD5**.
  - Lee bloques de 1 KB en offsets exponenciales:
    - Para $i = -1$: Offset = 0 (primer KB).
    - Para $i \in [0, 10]$: Offset = $1024 \ll (2 \times i)$ (desplazamiento de bits).
    - Se detiene al llegar al final del archivo o tras 10 iteraciones.
  - Genera el hash hexadecimal MD5.

#### Implementación en C# (.NET 10):
```csharp
public static class MediaHasher
{
    public static string GenerateKoreaderHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var md5 = System.Security.Cryptography.MD5.Create();
        var buffer = new byte[1024];

        for (int i = -1; i <= 10; i++)
        {
            long offset = (i == -1) ? 0 : 1024L << (2 * i);
            if (offset >= stream.Length) break;

            stream.Seek(offset, SeekOrigin.Begin);
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexStringLower(md5.Hash!);
    }
}
```

---

## 3. Parsing de Metadatos (`ComicInfo.xml`)

Stump parsea archivos `ComicInfo.xml` embebidos en los archivos `.cbz` / `.cbr` (definición de Anansi Project):
- **Campos leídos**:
  - `Title`, `Series`, `Number` (volumen/número flotante, ej. `20.1`), `Volume`.
  - `Summary`, `Notes`.
  - `Year`, `Month`, `Day`.
  - `Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`.
  - `AgeRating` (normalizado en Stump como entero de 0 a 18 para el control parental).
  - `Genre` (separado por comas).
  - `Tags` (persistidos en tabla normalizada `tags` y vinculados vía `media_tags`).

---

## 4. Pipeline de Miniaturas y Optimización de Imágenes

- **Caché en disco**: Directorio configurado (`thumbnails/`) almacenando imágenes procesadas por `book.id`.
- **Formatos soportados**: WebP (preferido por relación calidad/peso), JPEG y PNG.
- **Generación en background**: Cuando el escáner detecta un libro nuevo, encola un background job de generación de miniaturas con dimensionamiento adaptativo (`ScaledDimensionResize`), para que la API nunca se bloquee redimensionando imágenes en la primera visita del usuario.
