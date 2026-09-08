# Pendiente

Lo que falta, verificado contra el código.

## Backend

### Enlaces de los feeds bajo un prefijo

`Gateway:PathBase` existe como opción de configuración y `Program.cs` aplica `UsePathBase`
cuando está declarada, así que el **enrutado entrante** funciona bajo cualquier prefijo.

Falta la otra mitad: `OpdsService.FormatUrl` y `OpdsV2Service.FormatUrl` construyen rutas
absolutas desde la raíz —`/opds/{apiKey}/v1.2/...`— y `UsePathBase` no las reescribe,
porque solo afecta a la ruta de entrada. Servido bajo un prefijo, cada enlace del catálogo
apuntaría fuera del montaje y los clientes fallarían al navegar. Hay además cuatro rutas
absolutas más en `Infrastructure`.

Dos caminos: anteponer el `PathBase` en los métodos que construyen enlaces, o leer
`HttpContext.Request.PathBase` —que ASP.NET rellena solo— y hacerlo llegar a los servicios,
que hoy no reciben el contexto.

Mientras no se resuelva, el backend solo funciona correctamente servido desde la raíz de su
host: por subdominio, no por subpath.

### Miniaturas JXL

SkiaSharp no decodifica el formato. El `Fallback` de `ThumbnailService` sirve el original
sin redimensionar. Es una regresión frente a ImageSharp.

### Portada de PDF de solo texto

`PdfBookProcessor` extrae imágenes embebidas pero no rasteriza, así que un PDF de texto y
fuentes cuenta páginas y no genera portada.

### Cabecera `X-Auth-Sub` vacía en rutas públicas de plugin

En las rutas declaradas como públicas de un montaje, el validador responde 200 sin escribir
cabeceras de identidad. `auth_request_set` deja las variables vacías y la location inyecta
igualmente `proxy_set_header X-Auth-Sub $auth_sub;`, así que al backend le llega la cabecera
presente y con cadena vacía.

El backend debe tratar `X-Auth-Sub` vacío como **anónimo**, nunca como un usuario con
identificador vacío.

## Gateway

### Resuelto: propagación y saneado de las cabeceras `X-Auth-*`

El sistema de plugins cerró este punto. `ConfigRenderer.Plugins.cs:83-103` y
`ConfigRenderer.Routes.cs:254-258` emiten ya la inyección real —`proxy_set_header X-Auth-Sub
$auth_sub;` y equivalentes para tenant, user, role, perms, age y app—, y
`NginxSnippetWriter.cs:40-56` borra incondicionalmente toda la familia `X-Auth-*` en el
snippet `proxy_forward.conf`, que se incluye también en rutas con `requires_auth = 0`.

El orden es correcto: el include con el borrado precede a la inyección, y en nginx gana el
último `proxy_set_header` del mismo nivel. Ya no hay falsificación desde el cliente.

### La prueba DPoP no se verifica

El navegador firma correctamente —clave ECDSA P-256 no extraíble, JWK en el orden del
thumbprint de RFC 7638—, pero el servidor no la comprueba en ningún punto. La columna
`session.client_pubkey` existe y nunca se lee ni se escribe; `server_config.proof_required`
es una bandera sin consumidor.

La defensa contra el robo de cookie que la documentación describe no está activa. No es una
regresión del sistema de plugins: es deuda anterior y sigue abierta.

## Gateway — sistema de plugins

Hallazgos de la validación del sistema de montajes por prefijo. Todos verificados en código.

### Bloqueantes

#### `/api/plugins` queda sin autenticación

`Program.cs:106` monta `app.MapPluginEndpoints()` en la raíz, y
`src/Api/ProxyManagementEndpoints.cs:18` ya lo monta bajo el grupo `/auth`. Como
`GatewayAuthMiddleware.cs:23` solo protege rutas que empiezan por `/auth/`, la copia colgada
de la raíz queda abierta.

Kestrel escucha en `ListenAnyIP(8080)` (`Program.cs:52`), así que cualquier contenedor de la
red interna de Docker —incluido el propio DiarSpeicher— puede llamar a
`http://gateway:8080/api/plugins` y crear, modificar o borrar montajes sin credencial.
`DELETE /api/plugins/{id}` (`PluginEndpoints.cs:266`) borra además el almacenamiento del
plugin.

Arreglo: eliminar la línea 106 y dejar únicamente el montaje bajo `/auth`.

#### Activar o desactivar un plugin no toca nginx

Ni `PluginEndpoints.cs` ni `PluginLifecycleManager.cs` llaman a `ConfigRenderer.RenderConfig()`
ni a `NginxApplier.ApplyAsync`. Las únicas llamadas del repositorio están en `Program.cs:72` y
en `ConfigurationEndpoints.cs`.

Activar crea la base de datos, la clave y marca `is_enabled = 1`; el panel muestra
"Ejecutando" pero la location **no existe** hasta que alguien aplique la configuración a mano
o se reinicie el contenedor. Desactivar tampoco retira la location: el plugin sigue sirviendo
tráfico.

Arreglo: encadenar render + apply en los endpoints de creación, toggle y borrado, y revertir
la fila si el apply falla.

#### `data_dir` sin validar, con borrado recursivo

`PluginDatabaseInitializer.cs:13-15`, `PluginLifecycleManager.cs:140-142` y `:160-162` toman
`mount.DataDir` tal cual del cuerpo de la petición (`PluginEndpoints.cs:163-165`), sin
normalizar ni comprobar que quede bajo `plugins/`. `PluginLifecycleManager.cs:148` ejecuta:

```csharp
Directory.Delete(dir, recursive: true);
```

Un `data_dir` de `/data/nginxDB` borraría `gateway.db`, `jwt_ecdsa.key` y todos los plugins.
Combinado con el punto anterior, es borrado remoto desde la red interna.

Arreglo: `Path.GetFullPath` y verificar que el resultado quede bajo
`<GetDbDir()>/plugins/`.

#### Inyección de directivas nginx

`ConfigRenderer.Plugins.cs:34, 36, 84, 85, 94` interpola `prefix`, `realm`, `upstream` y
`slug` sin sanear. Solo se limpia `slug` para el nombre de variable (`:77`). Un `realm` o un
`prefix` con `{`, `}` o `;` cierra el bloque location y añade directivas arbitrarias.
`nginx -t` (`NginxApplier.cs:40-47`) frena lo sintácticamente inválido, no una inyección
sintácticamente válida.

Arreglo: lista blanca en el endpoint antes de persistir.

| Campo      | Validación                                     |
| ---------- | ---------------------------------------------- |
| `slug`     | `^[a-z0-9][a-z0-9_-]{0,63}$`                   |
| `realm`    | `^[a-z0-9][a-z0-9_-]{0,63}$`                   |
| `prefix`   | `^/[A-Za-z0-9._~/-]*/$`                        |
| `upstream` | `Uri.TryCreate` limitado a `http`/`https`      |

#### Sin validación de colisión de prefijos

No hay `UNIQUE` en `plugin_mount.prefix` (`DatabaseInitializer.cs:309-324`) ni comprobación
contra `/admin/`, `/auth/`, `/` ni contra la tabla `route`.

Un plugin en `/admin/` genera una location duplicada y **todo apply posterior de nginx falla**
hasta que alguien lo desactive por base de datos. Como activar no dispara apply, el fallo no
aparece al crearlo, sino más tarde y en un cambio no relacionado. Un plugin en `/auth/` pasa
`nginx -t` y secuestra el login del panel.

#### Fail-open en la revocación de sesión

`SessionCookieAuthenticator.cs:88-91` captura la excepción de la consulta a `plugin.db` y
continúa hasta `AuthResult.Success` en `:93`. Si la base está bloqueada o corrupta, una sesión
revocada se autoriza. Debe devolver `AuthResult.Fail`.

#### La clave de API llega al backend

`X-Auth-Key` no está en la lista de borrado de `NginxSnippetWriter.cs:48-55`, así que la clave
del cliente se reenvía al upstream. El contrato acordado dice que el backend nunca ve la
clave.

Arreglo: `proxy_set_header X-Auth-Key "";` en la location del plugin, tras el include. Nunca
en la del validador (`ConfigRenderer.Plugins.cs:45`), que la necesita.

#### ReDoS por patrón de credencial

`UrlPathKeyAuthenticator.cs:91` ejecuta `Regex.Match` con un patrón que viene de la base de
datos, sin `matchTimeout` y recompilando en cada petición. Cachear el `Regex` por montaje con
un timeout acotado.

### No bloqueantes

- `GatewayAuthMiddleware.cs:23` sigue con `path.StartsWith("/auth/")` literal. Funciona porque
  la resolución por montaje la hace nginx —cada montaje tiene su validador
  `location = /{realm}/auth/validate`, `ConfigRenderer.Plugins.cs:34`—, pero el middleware no
  conoce montajes. Es el arreglo natural del primer bloqueante.
- Patrones de e-reader cableados en el núcleo: `UrlPathKeyAuthenticator.cs:100-107` lleva
  `opds|koreader|kobo` embebido como fallback, justo lo que el contrato dice que debe declarar
  el plugin. El regex además no está anclado, así que casa la subcadena en cualquier posición
  de la URI.
- La revocación por `jti` no se hereda del chasis: `RevocationCache.cs:11` sigue siendo un
  diccionario global plano sin noción de montaje, y ningún plugin lo usa —hacen un `SELECT` a
  SQLite por petición (`SessionCookieAuthenticator.cs:79`)—. Si se le da uso, la clave debe ser
  `(slug, jti)`.
- Orden de borrado invertido en `PluginEndpoints.cs:265-276`: borra el almacenamiento antes que
  la fila. Si el `DELETE` falla, quedan fila y location apuntando a un plugin sin base de
  datos.
- El rollback de nginx (`NginxApplier.cs:69, :79`) restaura el fichero sin revertir
  `plugin_mount` ni notificar; `ApplyOutcome` no tiene canal para ello.
- Campos declarados y no consumidos: `CredentialSourceRule.SegmentIndex` (`PluginMount.cs:27`),
  `type: "query"` (documentado en `:25`, no implementado) y `OidcConfig.DisableLocalAuth`
  (`:36`).
- Estados PKCE de OIDC solo en memoria (`OidcService.cs:22`): un reinicio durante el flujo
  invalida los callbacks en vuelo. La ventana es de 15 minutos (`:48`).

## Gateway — supuestos de una sola aplicación

| Pieza                    | Supuesto                                                            |
| ------------------------ | ------------------------------------------------------------------- |
| `GatewayAuthMiddleware`  | Prefijo `/auth/` literal; rutas públicas en una lista compilada      |
| `JwtService`             | Clave en `static readonly`, una por proceso; ruta de clave fija      |
| `JwtService`             | Sin `iss` ni `aud`, y ambos con validación desactivada               |
| `RevocationCache`        | Diccionario global plano; `UNION` de tres tablas en una constante    |
| `RevocationCache.Reload` | Sin parámetros, atado a una única cadena de conexión                 |
| `DatabaseInitializer`    | Directorio y nombre de fichero constantes; esquema monolítico        |
| `ConfigRenderer`         | `/admin/` y `= /auth/validate` escritos en la plantilla              |
| `route`                  | Sin columna que asocie una ruta a un montaje                         |
| `NAV_ITEMS`              | Menú del panel como constante compilada                              |

### Menores

- `CredentialRevoker` lista dos tablas y `RevocationCache` tres; el esquema queda descrito
  en dos sitios.
- `CredentialRevoker` interpola el nombre de tabla en el SQL. Hoy es seguro porque la lista
  es constante.
- `RenderRoutes` lee las columnas por índice numérico y los comentarios que documentan esos
  índices están desincronizados.
- El rollback de configuración restaura el fichero de nginx pero no la base de datos.
- `RevocationCache.Reload` no vacía el diccionario antes de recargar, así que una revocación
  deshecha no se refleja.
