# Authentication: API key in the URL path

Clients that cannot send custom headers (e-readers, simple OPDS apps, Kobo firmware, KOReader) put a key in a path segment. The key authorizes the request at the gateway; the backend reads it only to repeat it in the links it generates.

Source: `src/DiarSpeicher.Api/Middleware/OpdsAuthMiddleware.cs`, `Endpoints/RequestIdentity.cs`, `Endpoints/OpdsEndpoints.cs`, `OpdsV2Endpoints.cs`, `KoboEndpoints.cs`, `KoReaderEndpoints.cs`.

## How the credential is transmitted

The key is one path segment, `{apiKey}`, placed after the protocol root:

| Protocol | URL form | File |
| --- | --- | --- |
| OPDS 1.2 | `/opds/{apiKey}/v1.2/...` | [opds-v1.md](opds-v1.md) |
| OPDS 2.0 | `/opds/{apiKey}/v2.0/...` and `/opds/{apiKey}/v2/...` | [opds-v2.md](opds-v2.md) |
| Kobo sync | `/kobo/{apiKey}/v1/...` (key is mandatory) | [kobo.md](kobo.md) |
| KOReader sync | `/koreader/{apiKey}/...` (key is mandatory) | [koreader.md](koreader.md) |

OPDS also exists without a key segment (`/opds/v1.2/...`, `/opds/v2.0/...`, `/opds/v2/...`); in that case the identity comes from [auth-gateway-headers.md](auth-gateway-headers.md) only. `/api/v1` and `/api/v2` have no key segment.

With `Gateway:PathBase` set, the prefix goes first: `/{PathBase}/opds/{apiKey}/v1.2/catalog`.

## What the backend does with the key

| Step | Behaviour |
| --- | --- |
| Extraction | Regex `^/(?:opds/([^/]+)/(?:v1\.2\|v2\.0)\|koreader/([^/]+)\|kobo/([^/]+))` on the unprefixed path; the match is stored as request item `OpdsApiKey`. The route value `apiKey` is the fallback (this covers `/opds/{apiKey}/v2/...`). |
| Validation | None. The backend never compares the key to a store. The gateway resolves it and supplies the `X-Auth-*` identity headers. |
| Use | Every generated link (OPDS feeds, OPDS 2.0 documents, Kobo initialization and download URLs) repeats `/opds/{apiKey}/...`, `/kobo/{apiKey}/...` so a client keeps browsing under the same URL. |
| Other channels | The `X-Auth-Key` header and an `api_key` query parameter are not read by the backend. They are credential channels for the gateway, which blanks the header before proxying. |

## Failure

The backend returns the same response as for a missing identity:

| Situation | Status | Body |
| --- | --- | --- |
| Key not resolved to an identity by the gateway (no `X-Auth-Sub`), path not public | `401` | `{"error":"Identity not resolved by the gateway"}` |
| Valid key, account lacks `AccessKoboSync` or `AccessKoreaderSync` | `403` | `{"error":"This account is not allowed to use Kobo sync."}` / `{"error":"This account is not allowed to use KOReader sync."}` |

## Protocols using it

[opds-v1.md](opds-v1.md), [opds-v2.md](opds-v2.md), [kobo.md](kobo.md), [koreader.md](koreader.md).
