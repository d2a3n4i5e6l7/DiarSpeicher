# Client connections

How each client connects to DiarSpeicher and which API surface it uses. Every protocol sits behind a gateway that resolves the caller's identity; the backend consumes that identity as headers (see [auth-gateway-headers.md](auth-gateway-headers.md)).

## Files

| File | Content |
| --- | --- |
| [native-api-v2.md](native-api-v2.md) | `/api/v2` JSON API: catalog, pages, files, progress, library administration, uploads, metadata |
| [komga.md](komga.md) | Komga-compatible `/api/v1` and `/api/v2/users/me` |
| [opds-v1.md](opds-v1.md) | OPDS 1.2 (Atom, OpenSearch, OPDS-PSE) under `/opds/v1.2` |
| [opds-v2.md](opds-v2.md) | OPDS 2.0 (JSON) under `/opds/v2.0` and `/opds/v2` |
| [kobo.md](kobo.md) | Kobo library sync under `/kobo/{apiKey}` |
| [koreader.md](koreader.md) | KOReader progress sync under `/koreader/{apiKey}` |
| [auth-gateway-headers.md](auth-gateway-headers.md) | `X-Auth-*` identity headers, permissions, public paths, `401` response |
| [auth-api-key-in-path.md](auth-api-key-in-path.md) | `{apiKey}` path segment for OPDS, Kobo and KOReader |
| [auth-basic.md](auth-basic.md) | HTTP Basic flow declared by the OPDS 2.0 authentication document |

## Client to protocol and authentication

| Client | Protocol file | Authentication files |
| --- | --- | --- |
| Komelia and other Komga-compatible readers | [komga.md](komga.md) | [auth-gateway-headers.md](auth-gateway-headers.md) |
| OPDS 1.2 readers (Chunky, Panels, Moon+ Reader) | [opds-v1.md](opds-v1.md) | [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md) |
| Readium-based readers and other OPDS 2.0 clients | [opds-v2.md](opds-v2.md) | [auth-basic.md](auth-basic.md), [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md) |
| KOReader, progress sync plugin | [koreader.md](koreader.md) | [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md) |
| KOReader, OPDS catalog browsing | [opds-v1.md](opds-v1.md) | [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md) |
| Kobo devices | [kobo.md](kobo.md) | [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md) |
| Scripts and other HTTP clients | [native-api-v2.md](native-api-v2.md) | [auth-gateway-headers.md](auth-gateway-headers.md) |

## URL summary

`{prefix}` is the value of `Gateway:PathBase` (environment `Gateway__PathBase`, compose variable `PATH_BASE`) as `/prefix`, or empty when the service is served from the root.

| Protocol | Base URL |
| --- | --- |
| Native API | `{prefix}/api/v2` |
| Komga | `{prefix}/api/v1`, `{prefix}/api/v2/users/me` |
| OPDS 1.2 | `{prefix}/opds/v1.2`, `{prefix}/opds/{apiKey}/v1.2` |
| OPDS 2.0 | `{prefix}/opds/v2.0`, `{prefix}/opds/v2`, `{prefix}/opds/{apiKey}/v2.0`, `{prefix}/opds/{apiKey}/v2` |
| Kobo | `{prefix}/kobo/{apiKey}` |
| KOReader | `{prefix}/koreader/{apiKey}` |
| Health | `{prefix}/health` |

The backend removes the prefix before routing and puts the prefix the request arrived with into every link it generates (OPDS feeds, OPDS 2.0 documents, Kobo URLs).

## Behavior common to all protocols

| Item | Behavior |
| --- | --- |
| Protected prefixes | `/opds`, `/api/v1`, `/api/v2`, `/koreader`, `/kobo`; `401` `{"error":"Identity not resolved by the gateway"}` without an identity |
| Catalog visibility | Age restriction and excluded libraries of the resolved user filter every list, search, page and download |
| Reading progress | One latest session per user and book; page numbers are 1-based and clamped to the page count; EPUB progress is stored as a rendered page and rescaled when the device profile changes |
| File downloads | HTTP Range supported |
