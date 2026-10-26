# Authentication: gateway identity headers

The backend process does not verify credentials. It reads the identity that the gateway in front of it resolves and forwards as `X-Auth-*` request headers. Every protected route in every protocol depends on this mechanism.

Source: `src/DiarSpeicher.Api/Middleware/GatewayIdentityMiddleware.cs`, `OpdsAuthMiddleware.cs`, `GatewayOptions.cs`, `src/DiarSpeicher.Core/Domain/Models/AuthUser.cs`.

## How the credential is transmitted

The gateway sends these headers to the backend. When a header is repeated, the last value is used.

| Header | Meaning | If absent |
| --- | --- | --- |
| `X-Auth-Sub` | Stable user id. Required for an identity to exist. The value `anonymous` (case-insensitive) is treated as no identity. | No identity is resolved. |
| `X-Auth-User` | User name. | The `X-Auth-Sub` value is used as the name. |
| `X-Auth-Role` | Comma-separated roles (`admin` is the role the server checks). | No roles. |
| `X-Auth-Perms` | Comma-separated permissions. `*` grants all. | No permissions. |
| `X-Auth-Age` | Integer age restriction. Overrides the stored value when it parses as an integer. | Stored value, if any. |

Server ownership is never read from a header. The first user the instance sees is stored as owner; the owner passes every role and permission check.

Per request the backend:

1. Reads `X-Auth-Sub`. If empty or `anonymous`, skips identity resolution.
2. Inserts or updates the matching row in its `Users` table (id and user name). A row with the same user name and a different id is re-keyed to the new id together with its reading sessions, preferences, age restrictions and library exclusions.
3. Builds the identity (`Id`, `Username`, `IsServerOwner`, roles, permissions, age restriction, excluded library ids) and uses it for all catalog filtering (age restriction and excluded libraries apply to every protocol).

## Prefixes protected

`OpdsAuthMiddleware` requires a resolved identity for any path that starts with one of these prefixes (case-insensitive, after the `Gateway:PathBase` prefix is removed):

| Prefix | Protocol file |
| --- | --- |
| `/opds` | [opds-v1.md](opds-v1.md), [opds-v2.md](opds-v2.md) |
| `/api/v1` | [komga.md](komga.md) |
| `/api/v2` | [native-api-v2.md](native-api-v2.md), and `GET /api/v2/users/me` of [komga.md](komga.md) |
| `/koreader` | [koreader.md](koreader.md) |
| `/kobo` | [kobo.md](kobo.md) |

`GET /health` is outside these prefixes and needs no identity.

## Failure

| Situation | Status | Body |
| --- | --- | --- |
| Protected prefix, no identity resolved, path not public | `401` | `{"error":"Identity not resolved by the gateway"}` (`application/json`) |
| Identity resolved, permission missing on a guarded route | `403` | `{"error":"<message>"}`, see the protocol file |

Permissions checked by the code:

| Permission | Where |
| --- | --- |
| `ManageLibrary` | Library/series/media mutations, `/api/v2/filesystem/*`, `/api/v2/trash/*`, `/api/v2/metadata/*` (except `status` and `cover`) |
| `ScanLibrary` | `POST /api/v2/libraries/{id}/scan` |
| `FileUpload` | `POST /api/v2/libraries/{id}/upload`, tus `POST` and `PATCH` on `/api/v2/files` |
| `CreateFolder` | Upload and tus creation when the target sub-folder does not exist |
| `AccessKoreaderSync` | Every route under `/koreader/{apiKey}` |
| `AccessKoboSync` | Every route under `/kobo/{apiKey}` |

## Public paths (anonymous identity)

A path that matches `Gateway:PublicPaths` is served without a resolved identity: the request continues with an anonymous identity (empty id, no roles, no permissions, age-restricted when unset) instead of receiving `401`. Matching is segment by segment, case-insensitive; `*` is exactly one segment, `**` is the rest of the path. The configured list is used when non-empty; otherwise the defaults apply:

```
/opds/v2.0/auth
/opds/*/v2.0/auth
/opds/v2/auth
/opds/*/v2/auth
```

The path is compared after the `Gateway:PathBase` prefix is removed. Configuration key: `Gateway:PublicPaths` (environment form `Gateway__PublicPaths__0`).

## Gateway path prefix

`Gateway:PathBase` (environment `Gateway__PathBase`, compose variable `PATH_BASE`) is the public prefix the gateway mounts the service under, without slashes. The backend strips it from the request path, so identity and prefix checks above see the unprefixed path. Generated links carry the prefix the request arrived with.

## Session endpoints with no effect

| Route | Response |
| --- | --- |
| `GET /api/v1/login/set-cookie` | `204`, no cookie written by the backend |
| `GET /api/v1/claim` | `200` `{"isClaimed":true}` |

## Protocols using it

All of them: [native-api-v2.md](native-api-v2.md), [komga.md](komga.md), [opds-v1.md](opds-v1.md), [opds-v2.md](opds-v2.md), [kobo.md](kobo.md), [koreader.md](koreader.md). Combined with [auth-api-key-in-path.md](auth-api-key-in-path.md) for the key-in-URL forms and [auth-basic.md](auth-basic.md) for the OPDS 2.0 authentication document.
