# Plan de migración del front a C# (Blazor WebAssembly + MudBlazor, .NET 11)

Estado: propuesta, sin código escrito. Sustituye al front React de [frontEnd/](../frontEnd/) por un cliente
Blazor WebAssembly compilado con AOT, y elimina Node, npm y Vite del repositorio.

Las estimaciones de tiempo son orientativas, para una persona. Las cifras de tamaño y de tiempo de publicación
se fijan al cerrar la Fase 0.

## 1. Objetivo y alcance

| Objetivo | Cómo se mide |
| --- | --- |
| Un solo toolchain (.NET) | `frontEnd/` eliminado; `lint.sh` y `format.sh` sin `pnpm` (`build.sh` ya no lo usa) |
| Paridad funcional con el front actual | Checklist de la sección 5 completa |
| Cliente sin IL descompilable | `RunAOTCompilation` + `WasmStripILAfterAOT` en Release |
| Un solo proyecto de cliente, sin host ni `Browser/` | `src/DiarSpeicher.Client` es el único proyecto del front (sección 2) |
| Sin ficheros JS propios en `wwwroot` | El JS imprescindible va embebido en C# (sección 3); `wwwroot` solo lleva `index.html` y CSS |
| Contratos compartidos con la API | Los DTO se escriben una vez, en `DiarSpeicher.Contracts` |

Fuera de alcance:
- El backend, salvo la extracción de contratos y el traslado de las fuentes.
- El motor propio de EPUB con estilos: proyecto aparte con su propio plan.

## 2. Arquitectura

| Proyecto | SDK | Contenido |
| --- | --- | --- |
| `src/DiarSpeicher.Client` | `Microsoft.NET.Sdk.BlazorWebAssembly` | Páginas, componentes, servicios y tema. AOT solo al publicar en Release |
| `src/DiarSpeicher.Contracts` | `Microsoft.NET.Sdk` | DTO compartidos con la API, sin Dapper |
| `tests/DiarSpeicher.Client.Tests` | `Microsoft.NET.Sdk` | bUnit + xUnit |

**Un solo proyecto de cliente. No hay host `Sdk.Web` ni subcarpeta `Browser/`.** El AOT lo aplica `dotnet publish -c Release`
y el `.wasm` lo ejecuta el navegador; el servidor solo entrega archivos estáticos. En producción se despliega el
`wwwroot` publicado, servido tras el Gateway.

En desarrollo, `dotnet run --project src/DiarSpeicher.Client` va interpretado y sin proxy al Gateway; el AOT se
comprueba con la salida de `dotnet publish -c Release`.

Estructura del cliente:

```
Program.cs
wwwroot/        index.html, css/, js/ (solo los puentes de la sección 3)
Api/            clientes tipados, uno por grupo de rutas
Auth/           DpopHandler, UnauthorizedHandler, SessionState, AuthenticationStateProvider, PermissionService
Layout/         MainLayout, NavMenu, LibrarySelector
Pages/          una página por ruta
Components/     MediaCard, HudFrame, PageHeader, SectionRow, CatalogToolbar, diálogos, UploadPanel
Reader/         lector de páginas y puente del EPUB
Theme/          IronBloodTheme.cs, css/iron-blood.css
```

Archivos de más de ~600 líneas se dividen en clases `partial` por funcionalidad (`.razor` + `.razor.cs`, o
`Pagina.Funcionalidad.cs`).

## 3. JavaScript que queda

No se publica ningún fichero `.js` propio. El JS imprescindible va como texto dentro de una clase C#
(`Interop/<Nombre>Js.cs`), se importa en memoria con `JSHost.ImportAsync` y un data URI, y `[JSImport]` enlaza sus
funciones. Ejemplo ya escrito y con el mismo patrón en `tvboxHealth/admin-ui/Interop/DpopJs.cs`.

| Puente | Clase | Motivo |
| --- | --- | --- |
| DPoP | `DpopJs` | Firma ECDSA P-256 con clave no extraíble en IndexedDB; solo existe dentro de WebCrypto. `ECDsa` de .NET lanza `PlatformNotSupportedException` en `browser-wasm` (probado con el SDK `11.0.100-rc.2.26504.105`, Debug) |
| Subidas TUS | `TusStorageJs` | Estado de subidas reanudables en IndexedDB / `localStorage` |
| Lector | `ReaderBridgeJs` | Pantalla completa, gestos, teclado y precarga del lector |
| epub.js | (fichero) | Librería de terceros de cientos de KB: embeberla como texto no es razonable. Es la **única excepción** y sigue como fichero vendido en `wwwroot`, sin npm, hasta decidir la opción B o C del lector EPUB |

Condición a comprobar en el Gateway: una política CSP que prohíba `data:` en `script-src` impediría el import en memoria.

Todo lo demás (HTTP, serialización, estado, caché, SSE) va en C#.

## 4. Fases

Cada fase termina con un criterio verificable y con el front React funcionando en producción.

### Fase 0 — Prototipo y comprobaciones (1 semana)

- Proyecto de cliente (sin host ni `Browser/`) con MudBlazor 9.11.0, login con DPoP y la página Usuarios.
- Verificar que el Gateway acepta la prueba DPoP generada desde el cliente nuevo.
- Verificar en el Gateway:
  - que las rutas reales (`/series/123`) caen en `index.html`; el front actual usa `HashRouter` y el router de
    Blazor no;
  - que `<base href>` respeta el prefijo del montaje;
  - que `.wasm` y `_framework` se sirven comprimidos.
- Medir descarga, arranque y tiempo de publicación.

Salida: cifras medidas y decisión de seguir. Si el tamaño no es aceptable, se mantiene React y el plan se para aquí.

### Fase 1 — Cimientos (1–2 semanas)

- `DiarSpeicher.Contracts` con:
  - `Core/Domain/Catalog/DiarSpeicherDtos.cs` y `FilesystemDtos.cs`;
  - `Core/Domain/Enums/DomainEnums.cs` (los DTO lo usan);
  - `Core/Domain/Models/EpubDeviceProfile.cs` y `Core/Domain/MangaBaka/MangaBakaDtos.cs`;
  - DTO de petición hoy dentro de los endpoints: `TusSessionRequest`, `MetadataImportInitRequest`,
    `MetadataImportCompleteRequest`.
- `Core` y el cliente referencian `Contracts`; se conservan los `JsonPropertyName`.
- Contextos de `System.Text.Json` generados por fuente en el cliente.
- Fuentes: de `frontEnd/public/fonts/` a una carpeta común, enlazada desde
  `DiarSpeicher.Infrastructure.csproj` (hoy apunta a `frontEnd/` en la línea 27) y desde el cliente.
- Autenticación:
  - `DpopHandler`: firma cada petición con `signRequestProof(method, path)`; si falla, envía sin prueba;
  - `UnauthorizedHandler`: ante 401 limpia la sesión y navega al login;
  - `SessionState`: `Login` (con `getPublicKeyBase64`), `RegisterFirstAdmin`, `Logout` (con `resetKeyPair`),
    `RefreshUser`;
  - `AuthenticationStateProvider` y `PermissionService` (sustituye a `hasPermission`).
- Los DTO del Gateway (login, usuario, usuarios y roles) se escriben a mano en el cliente.
- `MainLayout`, `NavMenu`, `LibrarySelector`, tema Iron Blood y las 11 rutas vacías.

Salida: login y navegación reales; tema revisado contra el brandbook.

### Fase 2 — Catálogo (2 semanas)

| Ruta | Página | Dificultad |
| --- | --- | --- |
| `/` | `Home.razor` | Baja |
| `/series` | `SeriesGrid.razor` | Baja |
| `/series/:id` | `SeriesDetail.razor` + partial | Alta |
| `/media/:id` | `MediaDetail.razor` | Media (dos contadores EPUB) |

Componentes: `MediaCard`, `SectionRow`, `CatalogToolbar`, `HudFrame`, `PageHeader`.

Salida: navegación completa de solo lectura.

### Fase 3 — Lector de páginas (1–2 semanas)

- `Reader.razor` (`/read/:mediaId`, fuera del layout) para CBZ, PDF, RAR y EPUB en modo imagen.
- RTL/LTR, modos, teclado, precarga, progreso y `ReaderSettingsPanel`.
- `ReaderBridgeJs` (módulo embebido en C#, sección 3).

Salida: progreso idéntico al actual en las pruebas de paridad.

### Fase 4 — Administración (3 semanas)

| Ruta | Página | Dificultad |
| --- | --- | --- |
| `/libraries` | `Libraries.razor` + partial | Alta |
| `/libraries/:id` | `LibraryDetail.razor` + partial | Alta |
| `/users` | `Users.razor` | Media |
| `/roles` | `Roles.razor` | Media |
| `/metadata` | `Metadata.razor` | Media |
| `/trash` | `Trash.razor` | Baja |

- Progreso de escaneo: hoy `EventSource` sobre `/api/v2/libraries/{id}/scan/stream`. En el cliente,
  `ScanProgressService` lee el SSE con `HttpClient` en streaming (`SetBrowserResponseStreamingEnabled`) y
  reconecta él mismo.
- Diálogos con `IDialogService`: carpeta del servidor, perfil EPUB, portada, renombrar, entradas ausentes,
  vista previa de borrado, coincidencia de metadata.

Salida: todas las operaciones de administración de la sección 5.

### Fase 5 — Subida TUS (1 semana)

- `TusUpload` en C#, `UploadPanel`, `UploadItemRow`, cola y reanudación con `TusStorageJs` (módulo embebido en C#, sección 3).

Salida: subida reanudable de un archivo grande tras cortar la red.

### Fase 6 — Lector EPUB (1–2 semanas)

- epub.js en `wwwroot`, llamado por interop. El puente JS descarga el libro él mismo, sin pasarlo por .NET.
- Temas, progreso por porcentaje y ajustes, como hoy.

Salida: lector EPUB equivalente al actual.

### Fase 7 — Corte (1 semana)

1. El cliente nuevo se publica bajo otra ruta del Gateway, con el mismo backend.
2. Pruebas con usuarios reales durante al menos un ciclo normal de uso.
3. Con la checklist completa, la ruta principal del Gateway pasa al cliente nuevo.
4. El front React se conserva una versión más de despliegue. Vuelta atrás: cambiar la ruta del Gateway.
5. Después se retiran `frontEnd/`, las reglas de React de [AGENTS.md](../AGENTS.md) y la parte de `pnpm` de
   `lint.sh` y `format.sh`.

Total orientativo: entre 10 y 15 semanas.

## 5. Checklist de paridad

| Área | Funciones |
| --- | --- |
| Sesión | Login, primer administrador, cierre de sesión, expiración por 401, permisos por rol |
| Catálogo | Home por secciones, «Seguir leyendo», cuadrícula de series, filtro por biblioteca, búsqueda, orden natural |
| Detalle | Ficha de serie con pestañas, ficha de libro, dos contadores EPUB, portada, renombrar serie |
| Lectura | Lector de páginas (RTL/LTR, modos), progreso, lector EPUB, ajustes, perfiles de dispositivo EPUB |
| Bibliotecas | Crear/editar/borrar, selector de carpeta, escaneo con progreso, vista previa, entradas ausentes, vista previa de borrado |
| Administración | Usuarios, roles y permisos, protocolos por usuario, metadata externa (MangaBaka), papelera y restauración |
| Subida | TUS reanudable, cola, progreso por archivo, errores |
| Transversal | Tema, teclado, mensajes de error legibles, rutas con `Gateway:PathBase` |

## 6. Build, calidad y pruebas

| Tema | Plan |
| --- | --- |
| Build | `dotnet publish` del cliente en Release; el `wwwroot` publicado se copia como estáticos |
| Recorte | Linker activo, contextos JSON por fuente, `InvariantGlobalization` si no hace falta ICU |
| Solución | `DiarSpeicher.Client`, `Contracts` y `Client.Tests` en `DiarSpeicher.slnx`; entran en `lint.sh` y `sonar.sh` |
| Pruebas | bUnit para componentes y servicios; tests de contrato entre cliente y API |
| Comentarios | Inglés, solo para el porqué no evidente |

## 7. Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Descarga inicial grande | Medir en Fase 0; compresión; carga diferida de ensamblados |
| Rutas reales frente al `HashRouter` actual | Comprobar el fallback a `index.html` en Fase 0 |
| DPoP exige JS | Puente mínimo propio (sección 3) |
| Reescritura de ~15.000 líneas | Checklist de paridad, fases y front antiguo en paralelo |
| Pérdida de las fuentes del rasterizador al retirar `frontEnd/` | Se trasladan en la Fase 1 |

## 8. Decisiones pendientes

1. Presupuesto de descarga aceptable (con la cifra de la Fase 0).
2. Tema claro: se porta o se retira (el brandbook prohíbe fondos claros).
3. Subida TUS tal cual o subida normal.
