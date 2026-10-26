# 06 · Cómo conectarse — por aplicación

Cada cliente entra por el Gateway, que valida la credencial y reenvía la petición al servicio
con cabeceras `X-Auth-*`. El servicio no publica ningún puerto y no tiene endpoints de login.

Índice de la documentación por cliente: [clients/README.md](clients/README.md).
Rutas de cada protocolo: [04-apis-y-protocolos.md](04-apis-y-protocolos.md).
Cómo se resuelve la identidad: [05-autenticacion-actual.md](05-autenticacion-actual.md).

## URL base

```
https://<host>[:<puerto>]/diarspeicher
```

`/diarspeicher` es el prefijo bajo el que el Gateway monta el servicio y forma parte de la
ruta que recibe este. Se antepone a todas las rutas de [04](04-apis-y-protocolos.md): por
ejemplo, `/diarspeicher/api/v1/libraries`. Una petición sin ese prefijo no llega al servicio.

## Credenciales

| Credencial            | Cómo se obtiene                                                                  |
| --------------------- | -------------------------------------------------------------------------------- |
| Sesión (JWT)          | `POST /plugins/diarspeicher/auth/login` con `{ "username", "password" }`          |
| Clave de API          | `POST /plugins/diarspeicher/auth/api-keys` con una sesión activa                  |
| Usuario y contraseña  | Los de la cuenta; HTTP Basic solo funciona si la cuenta tiene `basic_password` habilitado |

El login responde `200` con `{ "token", "expires_at", "user" }` y fija la cookie
`diarspeicher_session` (7 días). El `token` se envía como `Authorization: Bearer <token>`.

La creación de clave acepta un cuerpo opcional `{ "clientType", "expiresInDays" }` y responde
con la clave en claro, una sola vez, además de las URL ya construidas: `api_key`, `opds_url`,
`koreader_url` y `kobo_url`.

Detalle de cada mecanismo: [05-autenticacion-actual.md](05-autenticacion-actual.md#mecanismos-de-credencial).

## Sesión desde un cliente HTTP

```bash
curl -k -c ck.txt -H 'Content-Type: application/json' \
  -d '{"username":"<usuario>","password":"<contraseña>"}' \
  -X POST https://<host>:<puerto>/plugins/diarspeicher/auth/login

curl -k -b ck.txt https://<host>:<puerto>/diarspeicher/api/v2/libraries
```

Con Bearer en lugar de cookie:

```bash
TOKEN=$(curl -sk -H 'Content-Type: application/json' \
  -d '{"username":"<usuario>","password":"<contraseña>"}' \
  -X POST https://<host>:<puerto>/plugins/diarspeicher/auth/login \
  | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')

curl -k -H "Authorization: Bearer $TOKEN" https://<host>:<puerto>/diarspeicher/api/v1/libraries
```

## Komga, CDisplayEx, Komelia

Detalle: [clients/komga.md](clients/komga.md).

| Campo                   | Valor                                         |
| ----------------------- | --------------------------------------------- |
| URL del servidor        | `https://<host>[:<puerto>]/diarspeicher`      |
| Credencial              | `Authorization: Bearer <token>`, o `X-Auth-Key: <clave>`, o HTTP Basic |

El cliente consulta `/api/v1` y, al conectar, `GET /api/v2/users/me`, `GET /api/v1/claim` y
`GET /api/v1/login/set-cookie`; las tres existen.

Recorrido típico:

| Paso                | Ruta                                       | Devuelve                                  |
| ------------------- | ------------------------------------------ | ----------------------------------------- |
| Bibliotecas         | `/api/v1/libraries`                        | Bibliotecas visibles                      |
| Series              | `/api/v1/series`                           | `Page<T>` de series                       |
| Libros de una serie | `/api/v1/series/{id}/books`                | `Page<T>` de libros                       |
| Lista de páginas    | `/api/v1/books/{id}/pages`                 | `number`, `fileName`, `mediaType`, `width`, `height`, `sizeBytes` de cada página      |
| Una página          | `/api/v1/books/{id}/pages/{n}`             | Imagen                                    |
| Miniatura           | `/api/v1/books/{id}/thumbnail`             | Imagen                                    |
| Guardar progreso    | `PATCH /api/v1/books/{id}/read-progress`   | `204`, cuerpo `{"page":42,"completed":false}` |
| Borrar progreso     | `DELETE /api/v1/books/{id}/read-progress`  | `204`                                     |

## Lectores OPDS (Chunky, Panels, Moon+ Reader, Aldiko, Thorium)

Detalle: [clients/opds-v1.md](clients/opds-v1.md) · [clients/opds-v2.md](clients/opds-v2.md).

| Versión  | URL del catálogo                                          | Formato                   |
| -------- | --------------------------------------------------------- | ------------------------- |
| OPDS 1.2 | `https://<host>[:<puerto>]/diarspeicher/opds/v1.2/catalog` | Atom XML                  |
| OPDS 2.0 | `https://<host>[:<puerto>]/diarspeicher/opds/v2.0/catalog` | JSON (Readium / WebPub)   |

Hay dos formas de autenticarse, según lo que permita configurar la aplicación:

- **Clave en la URL.** Se inserta la clave tras `/opds`:
  `https://<host>[:<puerto>]/diarspeicher/opds/<apiKey>/v1.2/catalog`. Los enlaces de los
  feeds conservan la clave. Es la opción para aplicaciones sin campos de credenciales; la
  URL lista para pegar la devuelve `opds_url` al crear la clave.
- **Usuario y contraseña.** La aplicación envía HTTP Basic. El campo contraseña acepta la
  clave de API; con la contraseña real de la cuenta solo funciona si la cuenta tiene
  `basic_password` habilitado. Basic transmite la credencial sin cifrar: requiere HTTPS.

El Authentication Document de OPDS 2.0 es público:

```
GET /diarspeicher/opds/v2.0/auth   →   application/opds-authentication+json
```

## KOReader

Detalle: [clients/koreader.md](clients/koreader.md).

| Campo del plugin de sincronización | Valor                                                       |
| ---------------------------------- | ----------------------------------------------------------- |
| Servidor                           | `https://<host>[:<puerto>]/diarspeicher/koreader/<apiKey>`  |

La cuenta necesita el permiso `AccessKoreaderSync`. `koreader_url` devuelve la URL completa al
crear la clave.

## Kobo

Detalle: [clients/kobo.md](clients/kobo.md).

| Ajuste               | Valor                                                       |
| -------------------- | ----------------------------------------------------------- |
| `api_endpoint`       | `https://<host>[:<puerto>]/diarspeicher/kobo/<apiKey>`      |

La cuenta necesita el permiso `AccessKoboSync`. `kobo_url` devuelve la URL completa al crear
la clave.

## API nativa

Detalle: [clients/native-api-v2.md](clients/native-api-v2.md).

Base `https://<host>[:<puerto>]/diarspeicher/api/v2`. Credencial: cookie de sesión,
`Authorization: Bearer`, `X-Auth-Key` o HTTP Basic.

## Diagnóstico: de quién es el error

| Respuesta                                                | Origen   | Significa                                                         |
| -------------------------------------------------------- | -------- | ----------------------------------------------------------------- |
| `401` generado por nginx (HTML)                          | Gateway  | Credencial no reconocida; la petición no llegó al servicio        |
| `401` `{"error":"Identity not resolved by the gateway"}` | Servicio | La petición llegó sin `X-Auth-Sub`                                |
| `403` `{"error":"..."}`                                  | Servicio | La cuenta no tiene el permiso necesario ([05](05-autenticacion-actual.md#permisos)) |
| `404` de nginx                                           | Gateway  | Prefijo equivocado; falta `/diarspeicher`                         |
| `400 The plain HTTP request was sent to HTTPS port`      | Gateway  | Se usó `http://` contra un puerto HTTPS                           |
