# OPDS 2.0

JSON catalog with Readium-style publications, an authentication document and a progression endpoint. Source: `src/DiarSpeicher.Api/Endpoints/OpdsV2Endpoints.cs`, `src/DiarSpeicher.Infrastructure/Opds/OpdsV2Service*.cs`, `src/DiarSpeicher.Core/Domain/Opds/OpdsV2Models.cs`.

## Base paths

| Form | Base |
| --- | --- |
| Identity from gateway headers | `/opds/v2.0` or `/opds/v2` |
| API key in the path | `/opds/{apiKey}/v2.0` or `/opds/{apiKey}/v2` |

All four bases expose the same routes. With `Gateway:PathBase = reader` the bases start with `/reader`. Generated links always use the `v2.0` form (`/opds/v2.0/...` or `/opds/{apiKey}/v2.0/...`), prefixed with the path base the request arrived with, whichever base was requested.

Entry point for a client: `{base}/catalog`. Authentication document: `{base}/auth`.

## Authentication

| Mechanism | File |
| --- | --- |
| `X-Auth-*` identity headers resolved by the gateway | [auth-gateway-headers.md](auth-gateway-headers.md) |
| Key in the path | [auth-api-key-in-path.md](auth-api-key-in-path.md) |
| Authentication document declaring Basic and local flows | [auth-basic.md](auth-basic.md) |

`{base}/auth` is served without an identity by default. Every other route returns `401` `{"error":"Identity not resolved by the gateway"}` when no identity is resolved.

## Routes

`{base}` is one of the four bases. Media types: feed `application/opds+json`; publication `application/opds-publication+json`; authentication document `application/opds-authentication+json`.

| Method | Path | Purpose | Query parameters | Response |
| --- | --- | --- | --- | --- |
| GET | `{base}/auth` | Authentication document | none | authentication doc |
| GET | `{base}/catalog` | Root feed with navigation and groups | none | feed |
| GET | `{base}/search` | Search books | `query` | feed (`publications`, at most 20, not paged) |
| GET | `{base}/libraries` | Libraries as navigation links | none | feed |
| GET | `{base}/libraries/{id}` | Series of a library | `page` | feed, paged |
| GET | `{base}/libraries/{id}/browse` | Same as `libraries/{id}` | `page` | feed, paged |
| GET | `{base}/series` | All series | `page` | feed, paged |
| GET | `{base}/series/{id}` | Books of a series | `page` | feed, paged |
| GET | `{base}/books/browse` | All books, newest first | `page` | feed, paged |
| GET | `{base}/libraries/browse` | Alias of `books/browse` | `page` | feed, paged |
| GET | `{base}/books/latest` | Alias of `books/browse` | `page` | feed, paged |
| GET | `{base}/libraries/books/latest` | Alias of `books/browse` | `page` | feed, paged |
| GET | `{base}/books/keep-reading` | Books with an in-progress session (an EPUB counts only when it was read as rendered pages) | none | feed |
| GET | `{base}/libraries/keep-reading` | Alias of `books/keep-reading` | none | feed |
| GET | `{base}/books/{id}` | One publication, with `readingOrder` | none | publication |
| GET | `{base}/books/{id}/thumbnail` | Cover thumbnail | none | image |
| GET | `{base}/books/{id}/pages/{page}` | One page image | `zero_based` (default `true`) | image |
| GET | `{base}/books/{id}/progression` | Read reading progression | none | `application/json` |
| PUT | `{base}/books/{id}/progression` | Write reading progression | none | empty `200` |
| GET | `{base}/books/{id}/file` | Download the file | none | book MIME type, Range supported |

Literal segments (`browse`, `keep-reading`, `books/latest`) take precedence over `{id}`.

## Feed shape

```json
{
  "@context": "https://readium.org/webpub-manifest/context.jsonld",
  "metadata": { "title": "...", "numberOfItems": 0, "itemsPerPage": 20, "currentPage": 0 },
  "links": [ { "href": "...", "type": "application/opds+json", "rel": "self" } ],
  "navigation": [ { "href": "...", "type": "application/opds+json", "properties": { "title": "..." } } ],
  "publications": [],
  "groups": []
}
```

| Part | Content |
| --- | --- |
| Catalog `links` | `self`, `start`, `search` (`{base}/search{?query}`, `templated: true`) |
| Catalog `navigation` | Libraries, All Series, All Books (`books/browse`), Latest Books (`books/latest`), Keep Reading (`books/keep-reading`) |
| Catalog `groups` | `Keep Reading` and `Latest Books` (first 10), each present only when it has publications |
| Series, library, books feeds | `metadata.numberOfItems`, `itemsPerPage` (20), `currentPage`; links `self`, `start`, `previous`, `next` |
| Library and series list feeds | Entries in `navigation` with `properties.title` |
| Book list feeds | Entries in `publications` |

## Pagination

| Item | Value |
| --- | --- |
| Page size | 20, fixed |
| `page` | 0-based; missing or negative is `0` |
| Links | `self` (`?page=n`), `previous` when `page > 0`, `next` when `page < totalPages - 1` |

## Publication shape

```json
{
  "@context": "https://readium.org/webpub-manifest/context.jsonld",
  "metadata": {
    "title": "...", "identifier": "urn:uuid:{id}", "@type": "http://schema.org/ComicStory",
    "modified": "ISO-8601", "description": "...", "numberOfPages": 0, "readingProgression": "ltr"
  },
  "images": [ { "href": ".../books/{id}/thumbnail", "type": "image/jpeg" } ],
  "links": [
    { "href": ".../books/{id}", "type": "application/opds-publication+json", "rel": "self" },
    { "href": ".../books/{id}/file", "type": "<book MIME>", "rel": "http://opds-spec.org/acquisition" },
    { "href": ".../books/{id}/progression", "type": "application/json", "rel": "http://vaemendis.net/opds-pse/stream" }
  ],
  "readingOrder": [ { "href": ".../books/{id}/pages/{n}?zero_based=true", "type": "image/jpeg" } ]
}
```

`readingOrder` is filled only by `GET {base}/books/{id}` and lists pages `0..numberOfPages-1`. In feeds it is an empty array. `numberOfPages` is the rendered page count for EPUB. Null fields are omitted.

## Authentication document shape

```json
{
  "id": ".../v2.0/auth",
  "title": "DiarSpeicher OPDS 2.0",
  "authentication": [
    { "type": "http://opds-spec.org/auth/basic", "labels": { "login": "Username", "password": "Password" } },
    { "type": "http://opds-spec.org/auth/local", "labels": { "login": "Username", "password": "Password" } }
  ],
  "links": [ { "href": ".../v2.0/catalog", "type": "application/opds+json", "rel": "start" } ]
}
```

## Progression

| Call | Body | Success | Failure |
| --- | --- | --- | --- |
| `GET {base}/books/{id}/progression` | none | `200` `{"page":n,"percentage":0.0-1.0,"modified":"ISO-8601","device":"OPDS-2.0-Client"}` | `404` when the user has no session for the book, the book is hidden, or it is an EPUB with no rendered-page progress |
| `PUT {base}/books/{id}/progression` | `{"page":n,"percentage":0.0-1.0,"modified":"...","device":"..."}`; all fields optional | `200` empty body | `400` `Could not update progression` when the book is hidden/unknown or the user is anonymous or unregistered |

`PUT` uses `page` (default `1`, clamped to `1..totalPages`) and `percentage`; `modified` and `device` are accepted and ignored. The reading status becomes Finished when `page` reaches the last page. `GET` computes `percentage` as `page / totalPages` when both are known, otherwise the stored percentage. For EPUB the page is the rendered page and is rescaled when the device profile changes; the percentage written by KOReader or an e-reader is a separate progress and is never read or written here.

## Page and download behavior

| Item | Behavior |
| --- | --- |
| Page number | `zero_based=true` reads page `page + 1`; `false` reads `page`. Internal numbering is 1-based. |
| Reading progress | A page request records progress for a registered user (unlike OPDS 1.2 there is no `cover` switch on this route). |
| Missing book or page | `404` `Page {page} of book {id} not found` |
| Missing thumbnail | `404` `Thumbnail for book {id} not found` |
| Missing publication | `404` `Publication {id} not found` |
| Missing library or series feed | `404`, empty body |
| Missing file | `404` `Book {id} not found or unauthorized` |

## Client quirks handled by the code

| Quirk | Handling |
| --- | --- |
| Clients that fetch the authentication document before logging in | `/auth` is a public path by default |
| Clients that use either `/opds/v2.0` or `/opds/v2` | Both bases are routed, with and without the key segment |
| Clients that expect aliased browse URLs | `libraries/browse`, `books/latest`, `libraries/books/latest`, `libraries/keep-reading`, `libraries/{id}/browse` |
| Clients that stream pages | `readingOrder` page links and the `opds-pse/stream` link to the progression resource |

## Related

[opds-v1.md](opds-v1.md), [auth-gateway-headers.md](auth-gateway-headers.md), [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-basic.md](auth-basic.md).
