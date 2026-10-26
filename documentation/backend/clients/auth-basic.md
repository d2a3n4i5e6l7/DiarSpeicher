# Authentication: HTTP Basic (OPDS 2.0 authentication document)

The backend does not read an `Authorization` header and contains no Basic or password check. The only Basic-related surface in the process is the OPDS 2.0 authentication document, which tells a client which login flow to use; the credential itself is checked by the gateway, which then forwards [gateway identity headers](auth-gateway-headers.md).

Source: `src/DiarSpeicher.Infrastructure/Opds/OpdsV2Service.cs` (`GetAuthenticationDoc`), `Endpoints/OpdsV2Endpoints.cs`, `Middleware/GatewayOptions.cs`.

## How the credential is transmitted

| Item | Value |
| --- | --- |
| Document route | `GET /opds/v2.0/auth`, `/opds/{apiKey}/v2.0/auth`, `/opds/v2/auth`, `/opds/{apiKey}/v2/auth` |
| Document media type | `application/opds-authentication+json` |
| Flows declared | `http://opds-spec.org/auth/basic` and `http://opds-spec.org/auth/local`, both with labels `login` = `Username`, `password` = `Password` |
| Document `id` | URL of the document itself (`.../v2.0/auth`, with the key segment and path prefix when present) |
| Document `links` | One link: `rel` `start`, type `application/opds+json`, href `.../v2.0/catalog` |
| Credential carrier | Defined by the flow (`Authorization: Basic base64(login:password)` for the Basic flow). Evaluated at the gateway, not in this process. |

## Prefixes protected

The document route is public by default (see public paths in [auth-gateway-headers.md](auth-gateway-headers.md)), so a client without credentials can read it. All other routes under `/opds`, `/api/v1`, `/api/v2`, `/koreader` and `/kobo` need a resolved identity.

## Failure

The backend never answers `WWW-Authenticate`. A request without a resolved identity gets:

| Status | Body |
| --- | --- |
| `401` | `{"error":"Identity not resolved by the gateway"}` |

## Protocols using it

[opds-v2.md](opds-v2.md) (declares the flows). Any client that sends Basic credentials to the gateway for [opds-v1.md](opds-v1.md), [komga.md](komga.md) or [native-api-v2.md](native-api-v2.md) reaches the backend as [gateway identity headers](auth-gateway-headers.md).
