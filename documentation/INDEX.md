# Índice de Documentación: DiarSpeicher (.NET 10) & Gateway (tvboxHealth)

La documentación se encuentra organizada en dos fases independientes para permitir el trabajo en paralelo:

---

## 📁 Fase 1: Backend DiarSpeicher (.NET 10 / C# 13)
*Objetivo: Motor de medios de alto rendimiento, escáner con caché de mtimes, streaming de cómics/EPUB y catálogo OPDS v1.2 sobre SQLite (WAL).*

1. **[01_plan_y_arquitectura_net10.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/01_plan_y_arquitectura_net10.md)**
   - Correspondencia tecnológica (Stump Rust vs .NET 10 C# 13).
   - Plan de fases del backend e integración ligera con el Gateway.
2. **[02_base_de_datos_y_relaciones.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/02_base_de_datos_y_relaciones.md)**
   - Modelado de base de datos SQLite con Entity Framework Core 10 (`diarspeicher.db`).
   - Bibliotecas, Series, Libros (`Media`), Metadatos (`ComicInfo.xml`/EPUB), Sesiones de lectura y Caché de directorios (`scanned_directories`).
   - Métodos de filtrado `ForUser(...)` para control parental y bibliotecas excluidas.
3. **[04_motor_escaneo_archivos_y_mtimes.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/04_motor_escaneo_archivos_y_mtimes.md)**
   - Algoritmo de detección de cambios mediante `mtime` de directorios e inodes.
   - Ciclo de vida de archivos (`READY`, `MISSING`, `RECOVERED`, `ERROR`).
   - Recorrido con `FileSystemEnumerable` no bloqueante y background worker reactivo.
4. **[05_flujo_extraccion_libros.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/05_flujo_extraccion_libros.md)**
   - Traza de endpoints y streaming de páginas bajo demanda desde `.cbz`, `.cbr` y `.epub`.
   - Generación del catálogo OPDS v1.2 XML compatible con Komga y lectores móviles.
5. **[06_motores_de_medios_hashing_y_parsing.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/06_motores_de_medios_hashing_y_parsing.md)**
   - Procesadores por formato (`ZipArchive`, `SharpCompress`, `VersOne.Epub`).
   - Hashing doble: Stump SHA-256 (muestreo de 4 bloques) y KOReader MD5 exponencial ($1024 \ll 2i$).
   - Parsing y normalización de `ComicInfo.xml`.
6. **[07_catalogo_opds_v2_y_compatibilidad_komga.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/07_catalogo_opds_v2_y_compatibilidad_komga.md)**
   - Catálogo OPDS v2.0 (Readium JSON-LD / WebPub Manifest) con navegación, facetado y sincronización de progreso.
   - API REST compatible con Komga (`/api/v1/...`) con paginación Spring (`Page<T>`), páginas, miniaturas y tracking de lectura.
7. **[08_sincronizacion_koreader_kobo_y_api_v2.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_1_backend/08_sincronizacion_koreader_kobo_y_api_v2.md)**
   - Protocolo de sincronización KOReader (`/koreader/{apiKey}/...`) basado en `koreader_hash`.
   - Servidor de sincronización nativo Kobo (`/kobo/{apiKey}/...`) con inicialización, sincronización incremental y streaming EPUB.
   - API REST v2 nativa de Stump (`/api/v2/...`) con streaming, encolado de escaneo y motor web de recursos y TOC para EPUBs.

---

## 📁 Fase 2: Gateway & IAM (tvboxHealth)
*Objetivo: Panel "Real Apps" en el Gateway, módulo E-Reader, gestión centralizada de usuarios, OIDC y API Keys para OPDS.*

1. **[01_integracion_real_apps_y_ereader.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_2_gateway/01_integracion_real_apps_y_ereader.md)**
   - Arquitectura del panel "Real Apps" en el Gateway `tvboxHealth`.
   - Módulo **E-Reader Real**: gestión de usuarios y generación de API Keys para clientes de lectura (Panels, Chunky, KOReader).
   - Inyección de cabeceras de identidad (`X-Auth-Sub`, `X-Auth-Role`) hacia el backend DiarSpeicher.
2. **[03_autenticacion_oidc_y_permisos.md](file:///home/diarmund/projects/DiarSpeicher/documentation/fase_2_gateway/03_autenticacion_oidc_y_permisos.md)**
   - Especificación del flujo OIDC (PKCE S256, auto-registro Just-In-Time) y RBAC perimetral.
