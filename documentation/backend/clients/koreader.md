# KOReader progress sync

Reading-progress sync routes for the KOReader sync plugin. Documents are identified by the KOReader file hash stored per book (`KoreaderHash`). Source: `src/DiarSpeicher.Api/Endpoints/KoReaderEndpoints.cs`, `src/DiarSpeicher.Infrastructure/Sync/KoReaderService.cs`, `src/DiarSpeicher.Core/Domain/Sync/KoReaderDtos.cs`.

## Base path

`/koreader/{apiKey}` (the key segment is mandatory). With `Gateway:PathBase = reader` the base is `/reader/koreader/{apiKey}`. The sync server URL configured in KOReader is this base.

## Authentication

| Mechanism | File |
| --- | --- |
| Key in the path | [auth-api-key-in-path.md](auth-api-key-in-path.md) |
| `X-Auth-*` identity headers resolved by the gateway | [auth-gateway-headers.md](auth-gateway-headers.md) |

| Situation | Status | Body |
| --- | --- | --- |
| No resolved identity | `401` | `{"error":"Identity not resolved by the gateway"}` |
| Identity without permission `AccessKoreaderSync` (server owner and `*` pass) | `403` | `{"error":"This account is not allowed to use KOReader sync."}` |

The permission check is a filter on the whole group, so `users/auth` and `users/create` also return `403` without it.

## Routes

`{base}` is `/koreader/{apiKey}`. Responses are `application/json`.

| Method | Path | Purpose | Response |
| --- | --- | --- | --- |
| GET | `{base}/users/auth` | Credential check | `200` `{"authorized":"OK"}` |
| GET | `{base}/users/create` | Registration probe | `200` `{"authorized":"OK"}` |
| GET | `{base}/syncs/progress/{document}` | Read progress for a document hash | `200` progress object |
| PUT | `{base}/syncs/progress` | Write progress | `200` `{"document":"...","timestamp":n}` |

## Request and response shapes

### GET progress

`{document}` is the KOReader hash. Fields other than `document` are omitted when unknown.

```json
{ "document": "<hash>", "progress": "<string>", "percentage": 0.42, "timestamp": 1700000000000 }
```

| Case | Response |
| --- | --- |
| Hash unknown or hidden for the user | `200` `{"document":"<hash>"}` |
| Book known, no session | `200` `{"document":"<hash>"}` |
| Session exists | `200` with `progress` (stored KOReader progress string, or the last page number as text), `percentage` (0 to 1), `timestamp` (Unix milliseconds of the last update) |

### PUT progress

Request body:

```json
{ "document": "<hash>", "progress": "<string>", "percentage": 0.42, "device": "...", "device_id": "..." }
```

| Field | Rule |
| --- | --- |
| `document` | KOReader hash; must match a visible book |
| `progress` | Stored as is; when it parses as an integer it also becomes the last page |
| `percentage` | Float, must be `0.0` to `1.0`; stored as sent; `1.0` or more marks the book Finished, below that Reading. For an EPUB this is the percentage progress, independent of the rendered-page progress that Komga and OPDS keep; neither changes the other or the other's finished state |
| `device`, `device_id` | Accepted and not stored |

| Status | Case | Body |
| --- | --- | --- |
| `200` | Saved (creates or updates the user's latest session for the book) | `{"document":"<hash>","timestamp":<Unix ms of the server clock>}` |
| `400` | `percentage` outside `0.0` to `1.0` | The error message as a JSON string |
| `404` | No visible book with that hash | empty |
| `401`, `403` | See Authentication | |

## Client quirks handled by the code

| Quirk | Handling |
| --- | --- |
| The plugin probes `users/auth` and `users/create` while being configured | Both return `{"authorized":"OK"}` and are covered by the permission filter, so a missing permission shows up at configuration time |
| The plugin stores `percentage` as a short float | The shortest round-trip decimal form of the float is stored, so `0.225` stays `0.225` |
| A hash with no book | `GET` answers `200` with only `document`, so the plugin treats it as no remote progress |

## Related

[kobo.md](kobo.md), [opds-v1.md](opds-v1.md) (browse and download the same books), [auth-api-key-in-path.md](auth-api-key-in-path.md), [auth-gateway-headers.md](auth-gateway-headers.md).
