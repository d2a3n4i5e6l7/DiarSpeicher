# Komga-compatible API

JSON API shaped like Komga's `/api/v1`, for Komga-protocol readers (for example Komelia). Source: `src/DiarSpeicher.Api/Endpoints/KomgaEndpoints.cs`, `src/DiarSpeicher.Infrastructure/Komga/*.cs`, `src/DiarSpeicher.Core/Domain/Komga/KomgaDtos.cs`.

## Base paths

| Base | Content |
| --- | --- |
| `/api/v1` | Catalog, content and reading progress |
| `/api/v2/users/me`, `/api/v2/series/{id}/read-progress/tachiyomi` | Current user and tracker progress (the routes outside `/api/v1`) |

With `Gateway:PathBase = reader` the bases are `/reader/api/v1` and `/reader/api/v2/...`. There is no API-key path segment on these routes.

## Authentication

| Mechanism | File |
| --- | --- |
| `X-Auth-*` identity headers resolved by the gateway | [auth-gateway-headers.md](auth-gateway-headers.md) |

Failure: `401` `{"error":"Identity not resolved by the gateway"}`. The key-in-path form does not apply to this protocol. Catalog visibility (age restriction, excluded libraries) follows the resolved identity.

## Routes

All responses are `application/json` unless stated. `page` is 0-based; `size` defaults to `20` and is clamped to `1..100`. Other query parameters are ignored.

| Method | Path | Purpose | Query parameters | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v2/users/me` | Current user | none | user object |
| GET | `/api/v2/series/{id}/read-progress/tachiyomi` | Series progress for trackers (Mihon) | none | tracker progress, or `404` |
| PUT | `/api/v2/series/{id}/read-progress/tachiyomi` | Mark books up to a number as read and the rest as unread | none | `204`, or `404` |
| GET | `/api/v1/claim` | Server claim status | none | `{"isClaimed":true}` |
| GET | `/api/v1/login/set-cookie` | Session handshake | none | `204`, no body |
| GET | `/api/v1/libraries` | Visible libraries | none | array of library |
| GET | `/api/v1/libraries/{id}` | One library | none | library, or `404` |
| GET | `/api/v1/series` | Series list | `library_id`, `search`, `page`, `size` | page of series |
| GET | `/api/v1/series/{id}` | One series | none | series, or `404` |
| GET | `/api/v1/series/{id}/books` | Books of a series | `page`, `size` | page of book |
| GET | `/api/v1/series/{id}/thumbnail` | Thumbnail of the first book of the series | none | image, or `404` |
| GET | `/api/v1/books` | Book list | `search`, `page`, `size` | page of book |
| GET | `/api/v1/books/latest` | Books by creation time, newest first | `page`, `size` | page of book |
| GET | `/api/v1/books/{id}` | One book | none | book, or `404` |
| GET | `/api/v1/books/{id}/pages` | Page list | none | array of page |
| GET | `/api/v1/books/{id}/pages/{pageNumber}` | One page image | none | image |
| GET | `/api/v1/books/{id}/thumbnail` | Cover thumbnail | none | image |
| GET | `/api/v1/books/{id}/file` | Download the file | none | book MIME type, Range supported |
| PATCH | `/api/v1/books/{id}/read-progress` | Set reading progress | none | `204` |
| DELETE | `/api/v1/books/{id}/read-progress` | Clear reading progress | none | `204` |

## Request and response shapes

### Page envelope

```json
{
  "content": [],
  "pageable": { "pageNumber": 0, "pageSize": 20, "offset": 0, "paged": true, "unpaged": false },
  "totalElements": 0, "totalPages": 0, "last": true, "size": 20, "number": 0,
  "first": true, "numberOfElements": 0, "empty": true
}
```

`totalPages = ceil(totalElements / size)`; `last` is `page >= totalPages - 1`.

### Current user

```json
{
  "id": "...", "email": "<user name>", "roles": ["USER", "ADMIN"],
  "sharedAllLibraries": true, "sharedLibrariesIds": [], "labelsAllow": [], "labelsExclude": [],
  "ageRestriction": { "age": 12, "restriction": "ALLOW_ONLY" }
}
```

| Field | Value |
| --- | --- |
| `email` | The user name (no e-mail addresses exist) |
| `roles` | `USER` always; `ADMIN` added for the server owner or role `admin` |
| `sharedAllLibraries` | `true` when the user has no excluded libraries |
| `ageRestriction` | Omitted when the user has none; `restriction` is `ALLOW_ONLY` when unset-age content is restricted, otherwise `EXCLUDE` |

### Library

`id`, `name`, `root` (library path), `importComicInfoBook`, `importComicInfoSeries`, `importEpubBook`, `importEpubSeries` (all `true`), `unavailable` (`false`).

### Series

`id`, `libraryId`, `name`, `url` (path), `created`, `lastModified`, `fileLastModified` (ISO-8601), `booksCount`, `booksReadCount`, `booksUnreadCount` (`booksCount` minus read minus in progress), `booksInProgressCount` (read, in-progress and unread counts are per user, taken from the latest reading session of each book), `metadata` (`status` `ONGOING`, `title`, `summary`, `ageRating`, `genres` `[]`, `tags` `[]`), `deleted` (`false`).

### Book

| Field | Value |
| --- | --- |
| `id`, `seriesId`, `seriesTitle`, `libraryId`, `name`, `url`, `number` | Identifiers and names; `number` is `1` |
| `created`, `lastModified`, `fileLastModified` | ISO-8601 |
| `sizeBytes`, `size` | Bytes and `"{n.n} MB"` |
| `media` | `status` `READY`, `mediaType` (`application/epub+zip`, `application/vnd.comicbook+zip`, `application/vnd.comicbook-rar`, ...), `pagesCount`, `comment` |
| `metadata` | `title`, `summary`, `number`, `numberSort`, `authors` (`name`, `role` `writer`), `tags`, `isbn` |
| `readProgress` | Present when the user has a session (for an EPUB, when it was read as rendered pages): `page` (1-based), `completed`, `readDate`, `created`, `lastModified` |
| `deleted` | `false` |

### Page list item

`number` (1-based), `fileName`, `mediaType`, `width`, `height`, `sizeBytes`, `size` (always present; empty string when `sizeBytes` is unknown, binary units such as `1.5 MiB` otherwise). EPUB books list rendered pages: `fileName` `page_{nnnn}.webp`, `mediaType` `image/webp` (or the image type for image-only pages), dimensions from the device profile. When no stored measurement exists the list is measured on first request and stored; when measurement yields nothing, entries are `page_{nnnn}.jpg` with `image/jpeg`. A book with zero pages returns `[]`.

### Tracker progress (`/api/v2/series/{id}/read-progress/tachiyomi`)

`GET` returns `{"booksCount","booksReadCount","booksUnreadCount","booksInProgressCount","lastReadContinuousNumberSort","maxNumberSort"}`. Books are ordered by the number in their metadata, or by position when it is missing; `lastReadContinuousNumberSort` is the number of the last book of the unbroken run of finished books from the start. `PUT` takes `{"lastReadContinuousNumberSort":n}`: every book numbered up to `n` becomes Finished and the rest lose their progress.

### Reading progress

| Call | Body | Success | Failure |
| --- | --- | --- | --- |
| `PATCH /api/v1/books/{id}/read-progress` | `{"page":n}` or `{"page":n,"completed":true\|false}` or `{"completed":true}` | `204` | `400` `Could not update read progress` when the book is hidden/unknown or the user is anonymous or unregistered |
| `DELETE /api/v1/books/{id}/read-progress` | none | `204` | `404` when no session existed. For an EPUB only the rendered-page progress is cleared; the percentage written by e-reader protocols stays |

`page` is clamped to `1..totalPages`. The status becomes Finished when `completed` is `true` (the page becomes the last one and `page` may be omitted) or when the page reached is the last one, whatever `completed` says. A body with neither a page nor `completed:true` returns `400`. For EPUB the page is the rendered page.

## Page and download behavior

| Item | Behavior |
| --- | --- |
| Page number | `pageNumber` is 1-based, passed through as is |
| Reading progress | A page request records the page as progress for a registered user |
| Missing book or page | `404` `Page {pageNumber} of book {id} not found` |
| Missing thumbnail | `404` `Thumbnail for book {id} not found` |
| Series thumbnail | `404` `No books found for series {id}` when the series is empty |
| Missing file | `404` `Book {id} not found or unauthorized` |
| Page and download content type | Detected MIME type of the extracted page / of the file extension |

## Client quirks handled by the code

| Quirk | Handling |
| --- | --- |
| Komga clients call `GET /api/v2/users/me` right after connecting and treat a `404` as an invalid server | Route registered outside `/api/v1`, under the protected `/api/v2` prefix |
| Komga clients call `GET /api/v1/claim` | Always `{"isClaimed":true}` |
| Komga clients call `GET /api/v1/login/set-cookie` | Always `204` |
| Komga clients deserialize the page list strictly | `size` is always present in each page item |
| Komga identifies users by e-mail | `email` carries the user name |

## Related

[auth-gateway-headers.md](auth-gateway-headers.md), [native-api-v2.md](native-api-v2.md).
