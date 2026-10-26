# Kobo sync

Library sync for Kobo devices: EPUB entitlements, metadata, covers and file download. Source: `src/DiarSpeicher.Api/Endpoints/KoboEndpoints.cs`, `src/DiarSpeicher.Infrastructure/Sync/KoboService.cs`, `src/DiarSpeicher.Core/Domain/Sync/KoboDtos.cs`.

## Base path

`/kobo/{apiKey}` (the key segment is mandatory). With `Gateway:PathBase = reader` the base is `/reader/kobo/{apiKey}`. The service builds absolute URLs in its responses from the request: `{scheme}://{host}{path base}/kobo/{apiKey}/...`. The scheme honours `X-Forwarded-Proto`; the host is the request `Host` header.

## Authentication

| Mechanism | File |
| --- | --- |
| Key in the path | [auth-api-key-in-path.md](auth-api-key-in-path.md) |
| `X-Auth-*` identity headers resolved by the gateway | [auth-gateway-headers.md](auth-gateway-headers.md) |

| Situation | Status | Body |
| --- | --- | --- |
| No resolved identity | `401` | `{"error":"Identity not resolved by the gateway"}` |
| Identity without permission `AccessKoboSync` (server owner and `*` pass) | `403` | `{"error":"This account is not allowed to use Kobo sync."}` |

The permission check is a filter on the whole `/kobo/{apiKey}` group.

## Routes

All `GET`; `{base}` is `/kobo/{apiKey}`.

| Path | Purpose | Request header | Response |
| --- | --- | --- | --- |
| `{base}/v1/initialization` | Resource URL table | none | `200` JSON, header `x-kobo-apitoken: e30=` |
| `{base}/v1/library/sync` | One batch of library entitlements | `x-kobo-synctoken` | `200` JSON array; headers `x-kobo-synctoken`, `x-kobo-sync: continue` |
| `{base}/v1/library/{bookId}/metadata` | Metadata of one book | none | `200` book metadata, or `404` |
| `{base}/v1/books/{bookId}/thumbnail/{width}/{height}/{isGreyscale}/image.jpg` | Cover | none | image, or `404` |
| `{base}/v1/books/{bookId}/thumbnail/{width}/{height}/{quality}/{isGreyscale}/image.jpg` | Cover (quality variant) | none | image, or `404` |
| `{base}/v1/books/{bookId}/file/epub` | EPUB download | none | `application/epub+zip`, Range supported, or `404` |

`width`, `height`, `quality` are integers; `isGreyscale` is a string. They are accepted and not used: the stored thumbnail is returned as is, with the content type of its file extension.

## Request and response shapes

### Initialization

```json
{ "resources": {
  "image_host": "https://host/prefix",
  "image_url_template": "https://host/prefix/kobo/{apiKey}/v1/books/{ImageId}/thumbnail/{Width}/{Height}/{IsGreyscale}/image.jpg",
  "image_url_quality_template": "https://host/prefix/kobo/{apiKey}/v1/books/{ImageId}/thumbnail/{Width}/{Height}/{Quality}/{IsGreyscale}/image.jpg",
  "account_page": "https://www.kobo.com/account/settings",
  "assets": "https://storeapi.kobo.com/v1/assets",
  "book_detail_page": "https://www.kobo.com/{region}/{language}/ebook/{slug}",
  "categories": "https://storeapi.kobo.com/v1/categories",
  "configuration_data": "https://storeapi.kobo.com/v1/configuration"
} }
```

The top-level property name is serialized in camelCase (`resources`).

### Library sync

| Item | Behavior |
| --- | --- |
| Scope | Only books with extension `epub`, visible to the user, ordered by id ascending |
| Batch size | 100 |
| Sync token (`x-kobo-synctoken`) | Base64 of the text `offset:{n}` (more batches follow) or `offset:{n}:done` (last batch). The request token is decoded for `n`; a missing or unreadable token means `0`. |
| Continuation | `x-kobo-sync: continue` is set when more than 100 books remain; the client repeats the call with the returned token |
| Body | JSON array of entitlement containers |

Entitlement container (PascalCase keys):

| Key | Content |
| --- | --- |
| `BookEntitlement` | `Accessibility` `Full`, `ActivePeriod.From`, `Created`, `CrossRevisionId`, `Id`, `IsHiddenFromArchive`, `IsLocked`, `IsRemoved` (`false`), `LastModified`, `OriginCategory` `Purchase`, `RevisionId`, `Status` `Active`; ids equal the book id |
| `BookMetadata` | See below |
| `ReadingState` | Present when the user has a session: `Created`, `CurrentBookmark` (`LastModified`, `ProgressPercent`, `ContentSourceProgressPercent`, percent 0 to 100, `null` when no percentage is stored), `EntitlementId`, `LastModified`, `PriorityTimestamp`, `Statistics`, `StatusInfo` (`Status` `Reading` or `Finished`, `TimesStartedReading` `1`) |

Book metadata: `Categories`, `Contributors` (writers), `CoverImageId` (book id), `CrossRevisionId`, `Description`, `DownloadUrls` (one item: `DrmType` `None`, `Format` `EPUB`, `Size`, `Platform` `Generic`, `Url` to `{base}/v1/books/{id}/file/epub`), `EntitlementId`, `Genre` (first category or `General`), `Language` `en`, `RevisionId`, `Series` (`Id`, `Name`, `Number`, `NumberFloat`), `Title`, `WorkId`.

## Status codes

| Code | Case |
| --- | --- |
| `200` | Initialization, sync, metadata, thumbnail, file |
| `401` | No identity |
| `403` | Missing `AccessKoboSync` |
| `404` | Unknown or hidden book, missing thumbnail, missing file on disk |

## Client quirks handled by the code

| Quirk | Handling |
| --- | --- |
| Device keeps the configured endpoint for all calls | Every URL in initialization and download links includes the key and the path prefix |
| Device reads `x-kobo-apitoken` | Always `e30=` |
| Device asks for covers in two URL shapes | Both thumbnail routes are served |
| Device pages through large libraries | Token-based batching with `x-kobo-sync: continue` |

## Related

[koreader.md](koreader.md), [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md).
