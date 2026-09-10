# 06 · Cómo conectarse — por aplicación

> **Lee esto antes de tocar nada relacionado con autenticación o con probar endpoints.**
>
> DiarSpeicher **nunca se expone directamente**. Siempre está detrás de un **Gateway**
> (proyecto aparte, en `/home/diarmund/myprojects/Gateway`) que resuelve la identidad y
> reenvía la petición con cabeceras `X-Auth-*`.
>
> El contenedor de DiarSpeicher **no publica ningún puerto**: `docker ps` lo muestra con la
> columna `PORTS` vacía. Lo único alcanzable es el gateway, en `5050/tcp`, y **solo por
> HTTPS** —una petición HTTP a ese puerto devuelve
> `400 The plain HTTP request was sent to HTTPS port`.
>
> El backend **no tiene login**. No existe `/login`, ni `/api/v2/login`, ni verificación de
> contraseñas. Buscar un endpoint de login en `src/DiarSpeicher.Api/Endpoints/` y no
> encontrarlo **no significa que esté roto**: significa que la autenticación vive en el
> plugin del gateway. Ver [05-autenticacion-actual.md](05-autenticacion-actual.md).

## Las dos direcciones que hay que distinguir

| | Sirve | Ejemplo |
| --- | --- | --- |
| `https://<host>:5050/test/` | La **SPA** (interfaz web) | Devuelve el HTML de la aplicación |
| `https://<host>:5050/diarspeicher/` | El **backend** (todas las APIs) | `/diarspeicher/api/v1/libraries` |

Confundirlas da `404` desde nginx: `/test/api/v1/libraries` no existe.

El prefijo `/diarspeicher` **forma parte de la ruta que recibe el backend**: el montaje tiene
`strip_prefix` desactivado porque el backend usa `UsePathBase`. No quitarlo al construir URLs.

## Quién emite las credenciales

**El gateway, siempre.** Es el dueño de las tablas `reader_user`, `api_key` y `session`, que
viven en la base SQLite del plugin —no en la de DiarSpeicher. El backend ni las ve ni las
valida: recibe la identidad ya resuelta en cabeceras.

Generar una credencial en el backend no serviría de nada: el gateway la validaría contra su
propia tabla, no la encontraría, y la petición nunca pasaría del proxy.

---

## Si uso la interfaz web (SPA) — sesión con cookie

El flujo que usa el frontend, y el único validado de punta a punta.

```
POST https://<host>:5050/plugins/diarspeicher/auth/login
Content-Type: application/json

{"username":"diarmund","password":"<contraseña>"}
```

Responde `200` con un JWT **ES256** y deja la cookie de sesión:

```
set-cookie: diarspeicher_session=<jwt>; path=/; secure; samesite=lax; httponly
```
```json
{"token":"eyJ...","expires_at":"...","user":{"id":1,"username":"diarmund","role":"admin","is_admin":true}}
```

La cookie dura 7 días. A partir de ahí, cualquier ruta del backend responde con solo enviarla:

```bash
curl -k -c ck.txt -H 'Content-Type: application/json' \
  -d '{"username":"diarmund","password":"<contraseña>"}' \
  -X POST https://localhost:5050/plugins/diarspeicher/auth/login

curl -k -b ck.txt https://localhost:5050/diarspeicher/api/v2/libraries
```

La base de la ruta de auth está en `frontEnd/src/api/client.ts` como `AUTH_BASE`.

## Si uso Komga, CDisplayEx o Komelia — Bearer

DiarSpeicher replica la API REST de Komga en `/api/v1`, con el `Page<T>` de Spring
(`content`, `totalElements`, `totalPages`, `number`, `size`). Los clientes que ya hablan
Komga funcionan sin adaptadores.

Se autentican con el mismo JWT del login anterior, en la cabecera:

```
Authorization: Bearer <jwt>
```

```bash
TOKEN=$(curl -sk -H 'Content-Type: application/json' \
  -d '{"username":"diarmund","password":"<contraseña>"}' \
  -X POST https://localhost:5050/plugins/diarspeicher/auth/login \
  | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

curl -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:5050/diarspeicher/api/v1/libraries
```

**URL base a configurar en el cliente:** `https://<host>:5050/diarspeicher`

Recorrido típico, todo verificado funcionando:

| Paso | Ruta | Devuelve |
| --- | --- | --- |
| Bibliotecas | `/api/v1/libraries` | Lista de bibliotecas visibles |
| Series | `/api/v1/series` | `Page<T>` de series |
| Libros de una serie | `/api/v1/series/{id}/books` | `Page<T>` de libros |
| Lista de páginas | `/api/v1/books/{id}/pages` | Número, nombre y tipo de cada página |
| Una página | `/api/v1/books/{id}/pages/{n}` | `image/jpeg` extraído del CBZ |
| Miniatura | `/api/v1/books/{id}/thumbnail` | `image/webp` |
| Guardar progreso | `PATCH /api/v1/books/{id}/read-progress` | `204`, cuerpo `{"page":42,"completed":false}` |
| Borrar progreso | `DELETE /api/v1/books/{id}/read-progress` | `204` |

## Si uso la API v2 nativa o GraphQL — cookie o Bearer

Es lo que consume la SPA. Sirve igual con la cookie de sesión o con `Authorization: Bearer`.

- `https://<host>:5050/diarspeicher/api/v2/...`
- `https://<host>:5050/diarspeicher/graphql`

## Si uso OPDS — Basic auth, o clave en la URL

El backend expone los dos feeds y **ambos responden correctamente**:

| Versión | Ruta | Formato |
| --- | --- | --- |
| OPDS 1.2 | `/diarspeicher/opds/v1.2/catalog` | Atom XML |
| OPDS 2.0 | `/diarspeicher/opds/v2.0/catalog` | JSON-LD (Readium / WebPub) |

El *authentication document* de OPDS 2.0 es **ruta pública**, no exige credenciales, y
declara los flujos soportados:

```
GET /diarspeicher/opds/v2.0/auth   →   application/opds-authentication+json
```

### Qué rellena el usuario en el lector

Las apps OPDS (Chunky, Panels, Moon+ Reader, Aldiko, Thorium) presentan **tres campos**:

| Campo | Valor |
| --- | --- |
| URL del catálogo | `https://<host>:5050/diarspeicher/opds/v1.2/catalog` |
| Usuario | `diarmund` |
| Contraseña | su contraseña |

No hay campo para token ni para cabeceras. El estándar OPDS 1.2 delega la autenticación en
HTTP `Basic` sobre TLS —**no define API keys**; OPDS 2.0 añade el authentication document,
que declara flujos (`auth/basic`, `auth/local`, `oauth/password`), pero tampoco define claves.

La clave en la ruta (`/opds/{apiKey}/v1.2/...`) es una **convención de Komga y Kavita**, no
del estándar. Existe para los lectores que no permiten configurar credenciales. Cuando se usa,
el backend la reinyecta en cada enlace del feed para que la navegación siga bajo la misma URL
—pero **no la valida**: eso es del gateway.

### Estado actual

**Basic auth y clave en ruta todavía no autentican.** El gateway tiene ambos autenticadores
implementados, pero validan contra su tabla `api_key`, que hoy está vacía porque **no existe
aún el endpoint que emita claves**. Además, su `401` no incluye la cabecera
`WWW-Authenticate`, así que un lector OPDS no llega a pedir credenciales al usuario.

Mientras tanto, los feeds sí son accesibles con la cookie de sesión o con `Bearer`, que es
como se han validado.

## Si uso KOReader o Kobo — clave en la URL

- `https://<host>:5050/diarspeicher/koreader/{apiKey}`
- `https://<host>:5050/diarspeicher/kobo/{apiKey}`

Dependen del mismo mecanismo de clave en ruta, así que quedan pendientes hasta que el gateway
tenga el emisor de claves.

---

## Diagnóstico: de quién es el 401

Distinguirlo ahorra buscar el problema en el sitio equivocado.

| Respuesta | Origen | Significa |
| --- | --- | --- |
| HTML con `<center>nginx/1.31.5</center>` | **Gateway** | La petición no llegó al backend. Credencial no reconocida |
| JSON `{"error":"Identity not resolved by the gateway"}` | **Backend** | Llegó sin cabeceras `X-Auth-*` |
| `400 The plain HTTP request was sent to HTTPS port` | **Gateway** | Se usó `http://` en vez de `https://` |
| `404` de nginx | **Gateway** | Prefijo equivocado (típico: `/test/` en vez de `/diarspeicher/`) |

Los logs del backend (`docker logs diarspeicher`) solo muestran lo que **superó** el gateway.
Si una petición no aparece ahí, la cortó el proxy.

## Protocolos de autenticación del montaje

Se configuran en la UI del gateway, en *Editar Plugin → Protocolos de Autenticación*:

| Protocolo | Autenticador | Estado |
| --- | --- | --- |
| Cookie de sesión JWT (ECDSA aislada) | `session_cookie` | **Funcionando** |
| Cabecera `X-Auth-Key` / HTTP Basic | `header_basic_key` | Implementado; sin claves emitidas |
| Clave en URL (OPDS / KOReader / Kobo) | `url_path_key` | Implementado; sin claves emitidas |

Se prueban **en cadena**: el primero que resuelve identidad gana. Por eso la cookie y el
Bearer funcionan aunque los otros dos no encuentren nada.
