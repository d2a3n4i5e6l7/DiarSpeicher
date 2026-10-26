# Native API v2

JSON HTTP API under `/api/v2` for external clients and scripts: catalog browsing, page and file delivery, reading progress, library administration, uploads and metadata matching. Source: `src/DiarSpeicher.Api/Endpoints/DiarSpeicherEndpoints*.cs`, `FilesystemEndpoints*.cs`, `TrashEndpoints.cs`, `MetadataEndpoints*.cs`, `TusEndpoints*.cs`, `ApiResponses.cs`.

## Base path

`/api/v2`. With `Gateway:PathBase = reader` the base is `/reader/api/v2`. There is no API-key path segment. JSON property names are camelCase. One route of the Komga protocol, `GET /api/v2/users/me`, shares this prefix and is described in [komga.md](komga.md).

## Authentication

| Mechanism | File |
| --- | --- |
| `X-Auth-*` identity headers resolved by the gateway | [auth-gateway-headers.md](auth-gateway-headers.md) |

| Situation | Status | Body |
| --- | --- | --- |
| No resolved identity | `401` | `{"error":"Identity not resolved by the gateway"}` |
| Missing permission | `403` | `{"error":"This account is not allowed to manage libraries."}` (library and filesystem routes), `...to manage library metadata.` (metadata routes), `...to scan libraries.`, `...to upload files.`, `Uploads are disabled on this server.` |

Permissions: `ManageLibrary`, `ScanLibrary`, `FileUpload`, `CreateFolder`; the server owner and `*` pass all.

## Conventions

| Item | Value |
| --- | --- |
| Paged list envelope | `{"data":[],"total":0,"page":0,"pageSize":20,"totalPages":0}` |
| `page` | 0-based, negative becomes `0` |
| `pageSize` | Default `20`, clamped to `1..100` |
| Required query parameters | A missing required parameter (non-nullable value such as `deleteFile`, `deleteFiles`, `offset`) returns `400` |
| Error body | `{"error":"..."}` for errors built by the API; some `400`/`404` bodies are plain strings |
| Page numbers in `/media/{id}/page/{page}` | 1-based |

## System

| Method | Path | Purpose | Response |
| --- | --- | --- | --- |
| GET | `/api/v2/ping` | Liveness | `200` `"pong"` |
| GET | `/api/v2/health` | Health | `{"status":"healthy"}` |
| GET | `/api/v2/version` | Version | `{"semver":"0.1.0","rev":"net11"}` |
| GET | `/api/v2/claim` | Server claim status | `{"isClaimed":bool}` |
| GET | `/api/v2/auth/me` | Resolved identity | `{"id","username","isServerOwner","ageRestriction"}` |
| GET | `/health` (no prefix, no identity needed) | Service health | `{"status":"healthy","service":"DiarSpeicher","version":"1.0.0","database":"SQLite (WAL)"}` |

## Media

| Method | Path | Purpose | Query parameters | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v2/media` | All media | `page`, `pageSize`, `newestFirst` (default `false`) | paged media |
| GET | `/api/v2/media/keep-reading` | In-progress media | none | array of media |
| GET | `/api/v2/media/{id}` | One media | none | media, or `404` |
| GET | `/api/v2/media/{id}/page/{page}` | One page image | none | image; `Cache-Control: public, max-age=86400`; `404` when missing |
| GET | `/api/v2/media/{id}/thumbnail` | Cover thumbnail | none | image; `Cache-Control: public, max-age=86400`; `404` when missing |
| GET | `/api/v2/media/{id}/file` | Download | none | file MIME type, file name, Range supported; `404` when missing |
| PUT | `/api/v2/media/{id}/progress` | Write progress | none | `200` `{"updated":true}`, or `404` |
| DELETE | `/api/v2/media/{id}` | Delete a media record | `deleteFile` (bool, required) | `204`; `404`; `400` with `{"error"}`; needs `ManageLibrary` |

Media object: `id`, `name`, `size`, `extension`, `pages`, `status`, `hash`, `koreaderHash`, `path`, `seriesId`, `createdAt`, `metadata` (`title`, `summary`, `writers`, `genre`, `publisher`, `ageRating`, `number`), `currentPage`, `isCompleted`, and for EPUB `readPercent`, `renderedPage`, `renderedTotalPages`, `renderedCompleted`. Null members are omitted. For an EPUB, `isCompleted` and `readPercent` (0 to 100) come from the percentage its reader wrote and `currentPage` is the page stored by the e-reader side, when there is one; `renderedPage`, `renderedTotalPages` and `renderedCompleted` come from reading it as rendered page images (Komga, OPDS, page requests) and are present only when it was read that way. The two progresses are independent.

Progress request body:

```json
{ "page": 12, "percentage": 0.4, "isCompleted": false }
```

`percentage` and `isCompleted` are optional. `page` is clamped to `1..pages`. Without `isCompleted`, the status becomes Finished when the last page is reached. For an EPUB, a body with `percentage` (0 to 100) is the e-reader progress: it stores the percentage and Finished when `isCompleted` is `true` or the percentage reaches `100`, and does not touch the rendered page; a body without `percentage` is a rendered page. Requesting a page (`/media/{id}/page/{page}`) also records that page as progress for a registered user.

## Series

| Method | Path | Purpose | Query / body | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v2/series` | Series list | `libraryId`, `page`, `pageSize` | paged series |
| GET | `/api/v2/series/{id}` | One series | none | series, or `404` |
| GET | `/api/v2/series/{id}/media` | Media of a series | `page`, `pageSize` | paged media |
| GET | `/api/v2/series/{id}/thumbnail` | Series cover | none | image; `Cache-Control: public, max-age=86400`; `404` |
| PUT | `/api/v2/series/{id}` | Rename or describe | body `{"name","description"}`, null leaves unchanged | series; `ManageLibrary` |
| PUT | `/api/v2/series/{id}/thumbnail` | Use a media cover as series cover | body `{"mediaId"}` | `{"updated":true}`; `ManageLibrary` |
| POST | `/api/v2/series/{id}/thumbnail` | Upload a cover | `multipart/form-data`, first file; JPG, PNG or WebP | `{"updated":true}`; `400` otherwise; `ManageLibrary` |
| DELETE | `/api/v2/series/{id}/thumbnail` | Remove the custom cover | none | `204` or `404`; `ManageLibrary` |
| DELETE | `/api/v2/series/{id}` | Delete a series | `deleteFiles` (bool, required) | `204`, `404`, `400`; `ManageLibrary` |

Series object: `id`, `name`, `path`, `status`, `libraryId`, `mediaCount`, `description`, `coverUpdatedAt`, `metadata` (`source`, `externalId`, `title`, `summary`, `publisher`, `writers`, `genres`, `status`, `year`, `coverUrl`, `link`, `finalVolume`, `type`, `totalChapters`). Null members are omitted.

## Libraries and scans

| Method | Path | Purpose | Query / body | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v2/libraries` | Visible libraries | none | array of library |
| GET | `/api/v2/libraries/{id}` | One library | none | library, or `404` |
| POST | `/api/v2/libraries` | Create | body `{"name","path","description","emoji","config"}` | `201` library with `Location`; `400`; `ManageLibrary` |
| PUT | `/api/v2/libraries/{id}` | Update (null fields unchanged) | body as create, all optional | library, `404`, `400`; `ManageLibrary` |
| DELETE | `/api/v2/libraries/{id}` | Delete | `deleteFiles` (bool, required) | `204`, `404`, `400`; `ManageLibrary` |
| GET | `/api/v2/libraries/{id}/missing` | Missing series and volumes | none | `{"series":[],"orphanVolumes":0,"totalVolumes":0,"totalReadingSessions":0}` |
| POST | `/api/v2/libraries/{id}/purge-missing` | Remove missing entries | none | `{"removed":n}`; `ManageLibrary` |
| POST | `/api/v2/libraries/{id}/scan` | Queue a scan | none | `202` with `Location`, or `404`; `ScanLibrary` |
| GET | `/api/v2/libraries/{id}/scan` | Current scan snapshot | none | scan status, or `204` when none |
| GET | `/api/v2/libraries/scans` | All active scans | none | array of scan status |
| GET | `/api/v2/libraries/{id}/scan/stream` | Live scan progress | none | `text/event-stream` |
| POST | `/api/v2/libraries/{id}/upload` | Multipart upload into a library | query `subpath`; form field `subpath` | `{"uploadedCount","files":[{"name","path","size"}],"scanJobTriggered"}` |

Library object: `id`, `name`, `path`, `status`, `seriesCount`, `mediaCount`, `missingSeries`, `missingVolumes`, `description`, `emoji`, `createdAt`, `updatedAt`, `lastScannedAt`, `config` (`libraryType`, `libraryPattern`, `defaultReadingDir`, `defaultReadingMode`, `defaultLibraryViewMode`, `convertRarToZip`, `hardDeleteConversions`, `generateFileHashes`, `generateKoreaderHashes`, `processMetadata`, `watch`, `hideSeriesView`, `thumbnailWidth`, `thumbnailHeight`, `ignoreRules`).

Scan status: `libraryId`, `phase`, `completedSeries`, `totalSeries`, `currentSeries`, `completedMedia`, `totalMedia`, `pendingMedia`, `currentFile`, `readingFiles`, `createdMedia`, `createdSeries`, `currentMedia`, `currentSeriesId`, `message`, `percentage`, `elapsedSeconds`, `etaSeconds`, `finished`, `queued`.

Scan stream: each event is `data: {scan status JSON}` followed by a blank line; the stream ends after a snapshot with `finished: true`. A comment line `: ping` is sent every 20 seconds. Response headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache, no-transform`, `X-Accel-Buffering: no`.

Multipart upload errors: `400` not multipart, missing boundary or no accepted files; `403` uploads disabled or permission missing; `404` library not found; `413` file larger than `Storage:Upload:MaxFileUploadSize`.

## Resumable upload (tus 1.0.0)

Base `/api/v2/files`. Creation and termination extensions. Upload requires `FileUpload`.

| Method | Path | Purpose | Request headers | Response |
| --- | --- | --- | --- | --- |
| OPTIONS | `/api/v2/files`, `/api/v2/files/{id}` | Capabilities | none | `204`; `Tus-Resumable: 1.0.0`, `Tus-Version: 1.0.0`, `Tus-Extension: creation,termination`, `Tus-Max-Size` |
| POST | `/api/v2/files` | Create a session | `Upload-Length` (required, greater than 0), `Upload-Metadata` | `201` with `Location: /files/{id}` |
| HEAD | `/api/v2/files/{id}` | Offset | none | `200`; `Upload-Offset`, `Upload-Length`, `Cache-Control: no-store`; `404` |
| PATCH | `/api/v2/files/{id}` | Append bytes | `Content-Type: application/offset+octet-stream`, `Upload-Offset` | `204` with new `Upload-Offset`; `409` with the current `Upload-Offset` on mismatch; `415`; `400` |
| DELETE | `/api/v2/files/{id}` | Terminate | none | `204` |

`Upload-Metadata` keys (values Base64, comma-separated pairs): `libraryId` (required), `filename` (required, extension must be allowed), `subpath` (optional, must stay inside the library). Creation errors: `400` missing or invalid header or metadata, `403` uploads disabled / no `FileUpload` / no `CreateFolder` for a new folder, `404` library not found, `413` over `Storage:Upload:MaxFileUploadSize`, `500` destination not writable. When `Upload-Offset` reaches `Upload-Length` the file is moved into the library and a scan of that library is queued. The `Location` value is `/files/{id}` without the `/api/v2` prefix and without the path prefix.

## Chunked metadata dump import

| Method | Path | Purpose | Request | Response |
| --- | --- | --- | --- | --- |
| POST | `/api/v2/metadata/import/init` | Start a session | `{"fileName","totalBytes"}` (`.zst`, `.tar.gz`, `.tgz`) | `{"uploadId","chunkSize":52428800}`; `409` busy; `400` |
| POST | `/api/v2/metadata/import/chunk` | Append a chunk | query `uploadId`, `offset` (both required); raw body (up to 75 MiB) | `{"offset":n}`; `409` `{"error","currentOffset"}` on mismatch; `404`; `400` |
| POST | `/api/v2/metadata/import/complete` | Start the ingest | `{"uploadId","fileName"}` | `202`; `404`; `400`; `409`; `500` |
| POST | `/api/v2/metadata/import` | Single-request multipart import | `multipart/form-data` with the dump file | `202`, `400`, `409`, `500` |

All need `ManageLibrary`.

## Metadata matching

| Method | Path | Purpose | Query | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v2/metadata/status` | Ingest and catalog status | none | status object (any identity) |
| POST | `/api/v2/metadata/download` | Start a dump download | none | `202` with `Location: /api/v2/metadata/status`; `409`; `500`; `ManageLibrary` |
| GET | `/api/v2/metadata/cover` | Proxy a catalog cover | `url` (required) | image, `Cache-Control: public, max-age=604800`, or `404` (any identity) |
| GET | `/api/v2/metadata/series/{seriesId}/candidates` | Match candidates | `limit` (default `10`) | array of candidate; `ManageLibrary` |
| PUT | `/api/v2/metadata/series/{seriesId}/match/{mangaBakaId}` | Apply a match | none | `{"matched":true}` or `404`; `ManageLibrary` |
| DELETE | `/api/v2/metadata/series/{seriesId}/match` | Remove a match | none | `204` or `404`; `ManageLibrary` |

## Filesystem

All require `ManageLibrary`; paths must be inside the configured library roots, otherwise `403` `{"error":"Esa ruta esta fuera de las carpetas permitidas."}`.

| Method | Path | Purpose | Query / body | Response |
| --- | --- | --- | --- | --- |
| GET | `/api/v2/filesystem/roots` | Configured roots | none | array of `{path,name,mounted}` |
| GET | `/api/v2/filesystem/browse` | Sub-folders of a folder (hidden folders skipped, at most 1000) | `path` (default: first root) | `{path,parent,isRoot,entries,truncated}`; `404`; `403` |
| GET | `/api/v2/filesystem/preview` | Series a scan would create | `path`, `pattern` (`SeriesBased` or `CollectionBased`) | `{path,pattern,series,totalVolumes}` |
| GET | `/api/v2/filesystem/disk` | Disk usage | `path` | `{path,totalBytes,freeBytes,available}` |
| POST | `/api/v2/filesystem/folder` | Create a folder | `{"parent","name"}` | folder entry; `400` invalid name |
| GET | `/api/v2/filesystem/index` | Folder index status | none | status |
| POST | `/api/v2/filesystem/index` | Start an index refresh | none | `202` status; `409` when one is running |
| GET | `/api/v2/filesystem/search` | Search the folder index | `q`, `limit` (default `60`) | array of hit |
| GET | `/api/v2/filesystem/deletion` | Inspect what a delete would remove | `path` | `{path,name,fileCount,bytes,folders}`; `404`; `403` |
| DELETE | `/api/v2/filesystem/entry` | Move a file or folder to the trash | `path` | trash entry; `404`; `403`; `500` |

## Trash

All require `ManageLibrary`.

| Method | Path | Purpose | Response |
| --- | --- | --- | --- |
| GET | `/api/v2/trash/` | List entries | array of `{id,originalPath,name,isDirectory,fileCount,bytes,deletedAt,expiresInSeconds}` |
| POST | `/api/v2/trash/{id}/restore` | Restore | `204`; `400` when expired or the name is taken |
| DELETE | `/api/v2/trash/{id}` | Purge now | `204` or `404` |

## EPUB

| Method | Path | Purpose | Response |
| --- | --- | --- | --- |
| GET | `/api/v2/epub/{id}/toc` | Table of contents | `{"mediaId","title","items":[{"title","href","children"}]}`, or `404` |
| GET | `/api/v2/epub/{id}/resource/{*resourcePath}` | A resource inside the EPUB | the resource with its content type; `Cache-Control: public, max-age=86400`; `404` |
| GET | `/api/v2/users/{id}/epub-profiles` | Device profiles for rendered pages | array of profile (defaults when none stored) |
| PUT | `/api/v2/users/{id}/epub-profiles` | Replace profiles | `{"updated":true,"count":n,"affectedBooks":n}` |

`{id}` is a user id or `me`. Reading or writing another user's profiles needs the server owner or role `admin`.

## Related

[auth-gateway-headers.md](auth-gateway-headers.md), [komga.md](komga.md).
