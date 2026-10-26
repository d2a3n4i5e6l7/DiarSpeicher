# OPDS 1.2

Atom catalog with OpenSearch and OPDS-PSE page streaming. Source: `src/DiarSpeicher.Api/Endpoints/OpdsEndpoints.cs`, `src/DiarSpeicher.Infrastructure/Opds/OpdsService*.cs`, `OpdsXmlBuilder.cs`.

## Base paths

| Form | Base |
| --- | --- |
| Identity from gateway headers | `/opds/v1.2` |
| API key in the path | `/opds/{apiKey}/v1.2` |

Both forms expose the same routes. With `Gateway:PathBase = reader`, the bases are `/reader/opds/v1.2` and `/reader/opds/{apiKey}/v1.2`. Every `href` in a feed carries the prefix the request arrived with, and carries `/{apiKey}/` when the request did. A request that arrives without the prefix produces links without it.

Entry point for a client: `{base}/catalog`.

## Authentication

| Mechanism | File |
| --- | --- |
| `X-Auth-*` identity headers resolved by the gateway | [auth-gateway-headers.md](auth-gateway-headers.md) |
| Key in the path | [auth-api-key-in-path.md](auth-api-key-in-path.md) |

Failure: `401` `{"error":"Identity not resolved by the gateway"}`. Catalog visibility (age restriction, excluded libraries) follows the resolved identity.

## Routes

All `GET`. `{base}` is one of the bases above. Feed responses use `Content-Type: application/atom+xml;profile=opds-catalog;charset=utf-8`.

| Path | Purpose | Query parameters | Response |
| --- | --- | --- | --- |
| `{base}/catalog` | Root navigation feed | none | Atom |
| `{base}/search` | OpenSearch description | none | `application/opensearchdescription+xml;charset=utf-8` |
| `{base}/search/feed` | Search across libraries, series and books | `search` | Atom (single page, no paging links) |
| `{base}/keep-reading` | Books with an in-progress session (an EPUB counts only when it was read as rendered pages) | none | Atom |
| `{base}/libraries` | All visible libraries | `search` | Atom |
| `{base}/libraries/{id}` | Series of one library | `page` | Atom, paged |
| `{base}/series` | All series | `search`, `page` | Atom, paged |
| `{base}/series/latest` | Series ordered newest first | `page` | Atom, paged |
| `{base}/series/{id}` | Books of one series | `page` | Atom, paged |
| `{base}/books` | All books | `search`, `page` | Atom, paged |
| `{base}/books/latest` | Books ordered newest first | `page` | Atom, paged |
| `{base}/books/{id}/thumbnail` | Cover thumbnail | none | Image, type from the stored thumbnail file extension |
| `{base}/books/{id}/pages/{page}` | One page image | `zero_based` (default `true`), `cover` (default `false`) | Image, type of the extracted page |
| `{base}/books/{id}/file` | Download the file | none | Book MIME type, `Content-Disposition` with file name, HTTP Range supported |
| `{base}/books/{id}/file/{filename}` | Same download; `{filename}` is ignored | none | Same |

Root feed entries (ids): `keepReading`, `allSeries`, `latestSeries`, `allLibraries`, `allBooks`, `latestBooks`. Root links: `self`, `start`, `search` (type `application/opensearchdescription+xml`, href `{base}/search`).

## Pagination

| Item | Value |
| --- | --- |
| Page size | 20 entries, fixed |
| `page` | 0-based; missing or negative is `0` |
| Links | `self`, `start`, `previous` (when `page > 0`), `next` (when more pages exist and the page is full), all type `application/atom+xml;profile=opds-catalog;kind=navigation` |
| `search` | Carried over into `self`, `previous`, `next` of `series` and `books` feeds |

## Search

OpenSearch template: `{base}/search/feed?search={searchTerms}` (type `application/atom+xml;profile=opds-catalog;kind=acquisition`, UTF-8 in and out). An empty or blank `search` returns an empty feed titled `Search Results`. A non-empty term returns, in this order, matching libraries, series and books, without paging. Matching is a substring (`%term%`).

## Entry shapes

| Entry | Links |
| --- | --- |
| Library | `subsection` to `{base}/libraries/{id}` |
| Series | `subsection` to `{base}/series/{id}` |
| Book | `http://opds-spec.org/image/thumbnail` (`image/jpeg`) to `books/{id}/thumbnail`; `http://opds-spec.org/image` (`image/jpeg`) to `books/{id}/pages/0?zero_based=true&cover=true`; `http://opds-spec.org/acquisition` to `books/{id}/file/{url-encoded file name}`; OPDS-PSE stream link |

Book entries also carry `<content type="html">` (`{size} MiB - {extension}` plus the summary), `<summary>` when a summary exists, and one `<author>` element holding one `<name>` per writer.

Acquisition link `type` by extension: `epub` is `application/epub+zip`; `zip`, `cbz`, `rar`, `cbr` are `application/zip`; anything else is `application/octet-stream`.

### OPDS-PSE stream link

`rel="http://vaemendis.net/opds-pse/stream"`, namespace `http://vaemendis.net/opds-pse/ns` (prefix `pse`).

| Attribute | Value |
| --- | --- |
| `href` | `{base}/books/{id}/pages/{pageNumber}?zero_based=true`, with the literal template token `{pageNumber}` |
| `type` | `image/jpeg` |
| `pse:count` | Total pages (for EPUB, the rendered page count) |
| `pse:lastRead` | Last page read by the user, when one is recorded |
| `pse:lastReadDate` | Session update time, format `yyyy-MM-dd'T'HH:mm:sszzz`, when a session exists |

## Page and download behavior

| Item | Behavior |
| --- | --- |
| Page number | With `zero_based=true` the server reads page `page + 1`; with `zero_based=false` it reads `page`. Internal numbering is 1-based. |
| Reading progress | A page request without `cover=true` records progress (page, and Finished when the last page is reached) for a named, registered user. A failure to record is logged and the image is still returned. |
| `cover` | `true` skips progress recording. The binder parses `true`/`false` only; the cover link emitted in feeds uses `cover=true`. |
| Page out of range | `404` (page below 1, or above the page count for non-EPUB books) |
| Unknown or hidden book | `404` `Book {id} not found or unauthorized` (pages, file) |
| Missing page | `404` `Page {page} of book {id} not found` |
| Missing thumbnail | `404` `Thumbnail for book {id} not found` |
| Unknown library or series | `404` `Library {id} not found` / `Series {id} not found` |
| Download | `Content-Type` is the file MIME type: `epub` `application/epub+zip`, `cbz` `application/vnd.comicbook+zip`, `cbr` `application/vnd.comicbook-rar`, `zip` `application/zip`, `rar` `application/vnd.rar`, `pdf` `application/pdf` |

## Client quirks handled by the code

| Quirk | Handling |
| --- | --- |
| Clients that cannot keep headers across navigation (e-readers) | Key-in-path base; every link repeats the key and the path prefix |
| Download name | The acquisition href ends in the url-encoded file name; `{filename}` in the route is accepted and ignored |
| Strict XML parsers | The declaration states `encoding="UTF-8"`, matching the UTF-8 response body |
| Page-streaming readers | PSE link with `pse:count`, `pse:lastRead`, `pse:lastReadDate` and the `{pageNumber}` template |
| Zero-based and one-based page clients | `zero_based` query parameter |

## Related

[opds-v2.md](opds-v2.md), [auth-gateway-headers.md](auth-gateway-headers.md), [auth-api-key-in-path.md](auth-api-key-in-path.md).
