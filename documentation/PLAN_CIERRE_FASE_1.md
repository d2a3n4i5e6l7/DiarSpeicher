# Plan de Cierre de la Fase 1 (Backend DiarSpeicher)

> **Propósito**: llevar la Fase 1 desde su estado actual hasta "terminada y probable".
> Este documento vive **fuera** de `fase_1_backend/` porque no especifica funcionalidad
> nueva: es el plan de ejecución para cerrar lo que los documentos 01–10 ya especifican.

**Fecha de auditoría**: 8 de septiembre de 2026
**Método**: trazado documento por documento contra el código de `src/`, verificando
rutas registradas, entidades, paquetes y configuración reales.

---

## 1. Estado verificado

| Doc | Bloque | Estado |
|---|---|---|
| 01 | Arquitectura y plan | ⚠️ Su "Fase 2: Auth/Usuarios/OIDC" no se construyó |
| 02 | Base de datos | ⚠️ Falta `HashedPassword`, entidades `ApiKey` y `Session` |
| 04 | Escaneo y mtimes | ⚠️ Sin `FileSystemWatcher`/inotify |
| 05 | Extracción de libros | ✅ Completo |
| 06 | Motores de medios | ⚠️ Sin procesador PDF, sin pipeline de miniaturas |
| 07 | OPDS v2 + Komga | ⚠️ Faltan 3 rutas |
| 08 | KOReader / Kobo / API v2 | ⚠️ Falta 1 ruta, divergen 2 |
| 09 | Uploads | ⚠️ Configuración ausente, contrato de respuesta distinto |
| 10 | GraphQL | ❌ Sin empezar |

**Sólido y verificado**: modelo de datos y relaciones, filtros `ForUser`, SQLite en WAL,
escáner con caché de mtimes y ciclo `READY/MISSING/RECOVERED`, hashing doble
(Stump SHA-256 muestreado + KOReader MD5 exponencial), parsing de `ComicInfo.xml`,
OPDS v1.2 completo, API v2 de Stump completa con motor EPUB de TOC y recursos,
sincronización KOReader y Kobo operativa, subida multipart funcionando.

---

## 2. Decisión bloqueante previa: estrategia de esquema

Hoy el arranque usa `await db.Database.EnsureCreatedAsync()` y **no existe carpeta
`Migrations/`**. `EnsureCreated` crea el esquema solo si la base no existe: **no aplica
cambios a una base ya creada**.

Casi todo el Bloque B1 añade columnas y tablas. Sin resolver esto, esos cambios no
llegarán a `diarspeicher.db` y fallarán en tiempo de ejecución con "no such column".

Hay que elegir **antes de escribir código**:

| Opción | Cuándo tiene sentido | Coste |
|---|---|---|
| **A. Adoptar EF Core Migrations** | La base ya tiene datos que importan | Medio: `dotnet ef migrations add`, sustituir `EnsureCreated` por `MigrateAsync`, generar la migración inicial desde el esquema actual |
| **B. Borrar y recrear** | La base es solo de pruebas | Nulo: borrar `diarspeicher.db`, reescanear |

**Recomendación: Opción A.** La Fase 1 es el núcleo y va a seguir cambiando de esquema
(PDF, miniaturas, GraphQL). Sin migraciones, cada cambio obliga a perder los datos, y eso
impide probar en condiciones realistas — que es justo el objetivo.

---

## 3. Bloques de trabajo

Ordenados por dependencia. B1 desbloquea a B2 y B5.

### B0 — Migraciones EF Core

**Objetivo**: poder evolucionar el esquema sin perder datos.

- Añadir `Microsoft.EntityFrameworkCore.Tools` al proyecto de Infraestructura.
- Generar la migración inicial que refleje el esquema actual.
- Sustituir `EnsureCreatedAsync()` por `MigrateAsync()` en `Program.cs`.
- Verificar que `InitializeSqliteWalAsync()` sigue ejecutándose después.

**Aceptación**: arrancar contra una `diarspeicher.db` existente no pierde bibliotecas ni
medios, y `__EFMigrationsHistory` aparece poblada.

---

### B1 — Fundación de identidad

**Objetivo**: cerrar el doc 02 §2.1 y la "Fase 2" del doc 01. Es el bloque más importante:
hoy la autenticación es decorativa.

#### B1.1 Entidades

- `User`: añadir `HashedPassword`, `MaxSessionsAllowed`, `AvatarUpdatedAt`,
  `OidcIssuerId`, `OidcEmail`.
- Crear `ApiKey` (`Id`, `UserId`, `KeyHash`, `Name`, `ExpiresAt`, `CreatedAt`).
- Crear `Session` (`Id`, `UserId`, `ExpiresAt`, `CreatedAt`).
- Añadir las colecciones de navegación en `User` y sus `DbSet<>`.

#### B1.2 Validación real de credenciales

Estas dos son **vulnerabilidades activas**, no deuda técnica:

1. `TryBasicAuthAsync` descodifica la cabecera Basic, **descarta la contraseña** y
   autentica solo por nombre de usuario. Cualquiera que sepa un nombre entra.
2. `TryApiKeyAuthAsync`, si la clave no corresponde a ningún usuario, **fabrica un
   `AuthUser` con esa clave como identidad y devuelve `true`**. Cualquier valor autentica.

Trabajo:
- Hashing de contraseñas con un algoritmo con factor de coste (Argon2id o BCrypt).
  No usar SHA-256 desnudo.
- `TryBasicAuthAsync`: verificar la contraseña contra `HashedPassword`, en comparación
  de tiempo constante.
- `TryApiKeyAuthAsync`: si la clave no resuelve a un usuario, **devolver `false`**.
  Validar contra `ApiKey.KeyHash` y respetar `ExpiresAt`.
- Respetar `IsLocked` y `DeletedAt` en ambas rutas.

#### B1.3 Gestión de usuarios

Hoy `Users.Add` **no aparece en ningún punto del código**: no hay forma de crear un
usuario por API. `GET /api/v2/claim` solo consulta si el servidor está reclamado; no
reclama nada.

- `POST /api/v2/claim`: crea el usuario propietario en un servidor sin reclamar.
  Debe rechazar si ya existe alguno.
- CRUD mínimo de usuarios restringido a `IsServerOwner`.
- Emisión y revocación de API Keys (guardando solo el hash; la clave en claro se muestra
  una única vez).

**Aceptación**:
- Contraseña incorrecta → `401`.
- API key inexistente o caducada → `401`, no acceso anónimo.
- Usuario bloqueado o borrado → `401`.
- Reclamar dos veces → `409`.
- Con cero usuarios, el modo `default-owner` anónimo sigue funcionando (compatibilidad).

**Tests**: `AuthenticationTests.cs` cubriendo los cinco casos anteriores.

---

### B2 — Cierre de contratos REST

**Objetivo**: que las rutas documentadas existan y respondan lo documentado.

#### B2.1 Rutas ausentes

| Ruta | Doc | Nota |
|---|---|---|
| `GET /opds/v2.0/libraries/{id}` | 07 | Series de una biblioteca |
| `GET /opds/v2.0/series/{id}` | 07 | Libros de una serie |
| `GET /api/v1/books/latest` | 07 | Hoy cae en `/books/{id}` con `id="latest"` |
| `GET /koreader/{apiKey}/users/create` | 08 | Solo existe `/users/auth` |

Las dos de OPDS v2 son las de mayor impacto: sin ellas un cliente OPDS 2.0 lista
bibliotecas pero **no puede entrar en ninguna**. La navegación está rota.

#### B2.2 Divergencias Kobo — requieren decisión

| Documentado (doc 08) | Implementado |
|---|---|
| `/v1/library/{bookId}/file/{revisionId}` | `/v1/books/{bookId}/file/epub` |
| `/v1/images/{bookId}/cover.jpg` | `/v1/books/{id}/thumbnail/{w}/{h}/{greyscale}/image.jpg` |

La implementación parece **más cercana al protocolo Kobo real** que el documento. La
decisión correcta es validarlo contra un dispositivo Kobo y luego **corregir el documento**,
no el código. Registrar la decisión aquí una vez tomada.

#### B2.3 Contrato de subida

El doc 09 especifica objetos; la implementación devuelve cadenas:

```jsonc
// Documentado
"files": [{ "name": "Batman_01.cbz", "path": "/storage/...", "size": 34852910 }]
// Actual
"files": ["Batman_01.cbz"]
```

Alinear `StumpUploadResponseDto.Files` a `List<UploadedFileDto>`.

#### B2.4 Configuración de subida

Ninguna de las tres variables del doc 09 existe:

| Config | Variable de entorno | Defecto |
|---|---|---|
| `EnableUpload` | `DIAR_ENABLE_UPLOAD` | `true` |
| `MaxFileUploadSize` | `DIAR_MAX_FILE_UPLOAD_SIZE` | `524288000` (500 MB **por fichero**) |
| `AllowedExtensions` | `DIAR_ALLOWED_EXTENSIONS` | `.cbz,.cbr,.epub,.pdf,.zip` |

⚠️ El `Storage:Upload:MaxRequestBytes` existente **no cumple este requisito**: limita la
petición completa, no cada fichero, y no lleva los nombres ni las variables del documento.
Hay que añadir el límite por fichero además del de petición, y mover la lista blanca de
extensiones (hoy incrustada en `StumpV2Service`) a configuración.

**Aceptación**: con `EnableUpload=false` el endpoint responde `403`; un fichero por encima
de `MaxFileUploadSize` se rechaza sin escribir nada en disco; una extensión fuera de la
lista se rechaza.

---

### B3 — Motores de medios

#### B3.1 Procesador PDF

`AcceptedMediaExtensions` incluye `"pdf"` y la subida lo admite, pero **no existe
`PdfBookProcessor`**. Hoy un PDF se indexa con `Pages = 0` y sin portada, en silencio y
sin error — el peor modo de fallo, porque parece que funcionó.

- Implementar `PdfBookProcessor : IBookProcessor` (doc 06 sugiere PdfPig o SkiaSharp):
  conteo de páginas y renderizado de la primera como portada.
- Registrarlo en el contenedor junto a los otros tres.
- Alternativa si se descarta PDF en esta fase: **quitar `"pdf"`** de las extensiones
  aceptadas y de la lista blanca de subida, para que falle de forma visible.

#### B3.2 Pipeline de miniaturas

El doc 06 §4 pide WebP y redimensionado adaptativo (`ScaledDimensionResize`). Hoy la
portada se guarda **tal cual sale del archivo**: una portada de 4 MB se sirve como
miniatura de 4 MB.

- Redimensionar a un ancho máximo configurable preservando proporción.
- Codificar a WebP con JPEG de reserva.
- Mantener la generación dentro del escaneo (ya corre en segundo plano vía
  `ScanBackgroundService`, así que el requisito de "no bloquear la API" ya se cumple).

**Aceptación**: la miniatura generada pesa un orden de magnitud menos que la portada
original y `/media/{id}/thumbnail` la sirve con el MIME correcto.

---

### B4 — Escaneo reactivo

El doc 04 §4.2 pide `FileSystemWatcher` (inotify en Linux) con debouncing de 5–10 s. **No
existe**: hoy el escaneo solo se dispara manualmente o tras una subida.

- Servicio en segundo plano que observe la ruta de cada biblioteca.
- Acumular eventos y encolar en `IScannerQueue` tras el periodo de calma, para no
  reescanear mientras se copian ficheros grandes.
- Manejar el límite de watches de inotify en bibliotecas con muchos directorios
  (`fs.inotify.max_user_watches`) y degradar con un aviso claro si se agota.

**Aceptación**: copiar un `.cbz` en una biblioteca hace que aparezca en la API sin
intervención manual, y copiar veinte seguidos dispara **un** escaneo, no veinte.

---

### B5 — GraphQL (doc 10)

Bloque entero sin empezar: no hay paquete HotChocolate, ni esquema, ni `MapGraphQL`.
Depende de **B1**, porque el doc exige `[Authorize]` y `AuthUser` inyectado en el contexto.

- `HotChocolate.AspNetCore` + `HotChocolate.Data.EntityFramework`.
- **Queries**: `libraries`, `series`, `media`, `readingSessions`, `serverConfig`.
- **Mutations**: `createLibrary`, `editLibrary`, `deleteLibrary`, `scanLibrary`
  (canalizada a `IScannerQueue`), `updateReadingProgress`, `uploadBooks`.
- **Subscriptions**: `jobProgress(jobId)` por WebSocket, lo que exige que el escáner
  **emita progreso** — hoy `LibraryScanReport` solo se produce al terminar. Hace falta
  publicar eventos incrementales durante el escaneo.
- `ForUser(user)` aplicado en **cada** resolutor, incluidos los de relaciones anidadas.
- DataLoaders para evitar N+1 en `series.media`.

**Aceptación**: `series.media` sobre 50 series ejecuta un número constante de consultas,
no 51. Un usuario con biblioteca excluida no la ve por ninguna ruta del árbol GraphQL.

---

## 4. Orden recomendado

```
B0 (migraciones)
   └─> B1 (identidad)  ──> B5 (GraphQL)
   └─> B2 (contratos REST)
B3 (medios)     ─ independiente
B4 (escaneo)    ─ independiente
```

**B0 primero, sin excepción**: sin migraciones, B1 no puede llegar a una base con datos.

**B1 segundo** porque es el que más riesgo elimina: cierra dos vulnerabilidades activas y
desbloquea GraphQL. Además, hasta que exista un usuario real no se pueden probar en serio
el progreso de lectura, el "keep reading" ni la sincronización con lectores.

**B2 en paralelo** con B1: son rutas independientes, sin acoplamiento.

**B3 y B4 al final**: degradan la experiencia pero no rompen nada, y no bloquean a nadie.

**B5 el último**: es el bloque más grande y el único que depende de otro.

---

## 5. Criterios de "Fase 1 terminada"

La fase se considera cerrada cuando **todos** se cumplen:

- [ ] Toda ruta documentada en los docs 05, 07, 08 y 09 existe y responde el contrato documentado.
- [ ] Las divergencias Kobo están resueltas y el documento refleja la decisión tomada.
- [ ] Una contraseña incorrecta o una API key inválida producen `401`.
- [ ] Se puede crear el usuario propietario por API y emitir API Keys.
- [ ] Un `.pdf` se indexa con páginas y portada, o deja de aceptarse de forma explícita.
- [ ] Las miniaturas se sirven redimensionadas y recomprimidas.
- [ ] Copiar un fichero en una biblioteca dispara el escaneo automáticamente.
- [ ] `/graphql` sirve Queries, Mutations y Subscriptions con `ForUser` en todos los niveles.
- [ ] La suite de tests pasa y cubre cada criterio anterior.
- [ ] Un escaneo de una biblioteca grande (≥500 tomos) completa sin errores y con uso de
      memoria estable.

---

## 6. Riesgos y decisiones abiertas

**Rutas Kobo**: hasta validar contra un dispositivo real no se sabe si manda el documento o
el código. Resolver antes de tocar `KoboEndpoints`.

**Elección de librería PDF**: PdfPig es puro .NET (sin dependencias nativas) pero renderizar
la portada requiere trabajo extra; SkiaSharp renderiza mejor pero arrastra binarios nativos
que complican el despliegue. Decidir según cómo se vaya a empaquetar el servidor.

**Límite de watches de inotify**: en bibliotecas con miles de directorios, B4 puede agotar
`fs.inotify.max_user_watches` (8192 por defecto en muchas distribuciones). Requiere o bien
ajuste del sistema o bien observar solo las raíces de biblioteca.

**Progreso incremental para GraphQL**: la suscripción `jobProgress` obliga a cambiar el
escáner para emitir eventos durante la ejecución, no solo el informe final. Es un cambio en
`LibraryScannerService`, no solo en la capa GraphQL — conviene tenerlo en cuenta al estimar B5.

**Huérfanos en la caché de páginas**: al borrar o reemplazar un `Media`, sus páginas
cacheadas quedan en disco hasta que el LRU las alcanza. Con el presupuesto por defecto de
2 GiB rara vez molesta; si la biblioteca rota mucho, hace falta invalidación por prefijo de
ruta al eliminar un medio.
