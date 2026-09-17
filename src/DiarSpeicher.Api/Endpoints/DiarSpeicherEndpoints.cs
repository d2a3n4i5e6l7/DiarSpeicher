using System.Runtime.CompilerServices;
using System.Text.Json;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Api.Endpoints;

public static class DiarSpeicherEndpoints
{
    private const string AuthUserKey = "AuthUser";

    private static readonly JsonSerializerOptions ScanJson =
        new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// Una linea que empieza por ":" es un comentario de SSE: EventSource la descarta y a
    /// nginx le basta para no dar por muerta la conexion en su proxy_read_timeout.
    /// </summary>
    private static async Task SendHeartbeatsAsync(HttpResponse response, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                await response.WriteAsync(": ping\n\n", ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // El cliente se fue o el flujo se cerro: no hay nada que salvar.
        }
    }

    private static DiarSpeicherScanStatusDto ToScanDto(ScanSnapshot snapshot) => new()
    {
        LibraryId = snapshot.Progress.LibraryId,
        Phase = snapshot.Progress.Phase.ToString(),
        CompletedSeries = snapshot.Progress.CompletedSeries,
        TotalSeries = snapshot.Progress.TotalSeries,
        CurrentSeries = snapshot.Progress.CurrentSeries,
        CompletedMedia = snapshot.Progress.CompletedMedia,
        TotalMedia = snapshot.Progress.TotalMedia,
        CurrentMedia = snapshot.Progress.CurrentMedia,
        CurrentSeriesId = snapshot.Progress.CurrentSeriesId,
        Message = snapshot.Progress.Message,
        Percentage = snapshot.Progress.Percentage,
        ElapsedSeconds = (int)snapshot.Elapsed.TotalSeconds,
        EtaSeconds = snapshot.Eta is { } eta ? (int)eta.TotalSeconds : null,
        Finished = snapshot.Finished,
        Queued = snapshot.Queued
    };

    public static RouteGroupBuilder MapDiarSpeicherEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2");

        MapSystemRoutes(group);
        MapMediaRoutes(group);
        MapSeriesRoutes(group);
        MapLibraryRoutes(group);
        MapEpubRoutes(group);
        MapUserProfileRoutes(group);

        return group;
    }

    private static void MapSystemRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/ping", () => Results.Ok("pong"));
        group.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        group.MapGet("/version", () => Results.Ok(new { semver = "0.1.0", rev = "net10" }));
        group.MapGet("/claim", async (
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var status = await service.GetSystemStatusAsync(ct);
            return Results.Ok(new { isClaimed = status.IsClaimed });
        });

        group.MapGet("/auth/me", (HttpContext httpContext) =>
        {
            var user = httpContext.Items[AuthUserKey] as AuthUser;
            if (user == null) return Results.Unauthorized();

            return Results.Ok(new
            {
                id = user.Id,
                username = user.Username,
                isServerOwner = user.IsServerOwner,
                ageRestriction = user.AgeRestriction
            });
        });
    }

    private static void MapMediaRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/media", async (
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool newestFirst = false,
            CancellationToken ct = default) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetMediaAsync(user, page, pageSize, newestFirst, ct);
            return Results.Ok(result);
        });

        group.MapGet("/media/keep-reading", async (
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetKeepReadingAsync(user, ct);
            return Results.Ok(result);
        });

        group.MapGet("/media/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetMediaByIdAsync(user, id, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/media/{id}/page/{page:int}", async (
            string id,
            int page,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var extracted = await service.GetMediaPageAsync(user, id, page, ct);
            if (extracted == null) return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(extracted.Data, extracted.ContentType.ToMimeType());
        });

        group.MapGet("/media/{id}/thumbnail", async (
            string id,
            HttpContext httpContext,
            [FromServices] DiarSpeicher.Infrastructure.Opds.IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var (data, contentType) = await opdsService.GetBookThumbnailAsync(user, id, ct);
            if (data == null) return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(data, contentType);
        });

        group.MapGet("/media/{id}/file", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var file = await service.GetMediaFileAsync(user, id, ct);
            if (file == null) return Results.NotFound();

            return Results.File(
                file.Value.Path,
                contentType: file.Value.ContentType,
                fileDownloadName: Path.GetFileName(file.Value.Path),
                enableRangeProcessing: true);
        });

        group.MapPut("/media/{id}/progress", async (
            string id,
            [FromBody] DiarSpeicherUpdateProgressInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var success = await service.UpdateProgressAsync(user, id, input, ct);
            return success ? Results.Ok(new { updated = true }) : Results.NotFound();
        });
    }

    private static void MapSeriesRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/series", async (
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            [FromQuery] string? libraryId = null,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetSeriesAsync(user, libraryId, page, pageSize, ct);
            return Results.Ok(result);
        });

        group.MapGet("/series/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetSeriesByIdAsync(user, id, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPut("/series/{id}", async (
            string id,
            [FromBody] DiarSpeicherUpdateSeriesInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.UpdateSeriesAsync(user, id, input, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        }).RequireManageLibrary();

        // La portada la ve cualquiera que ya puede ver la serie; cambiarla es gestion.
        group.MapGet("/series/{id}/thumbnail", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var cover = await service.GetSeriesThumbnailAsync(user, id, ct);
            if (cover is null) return Results.NotFound();

            // Sin revalidar: la URL lleva un testigo que cambia al cambiar la portada.
            httpContext.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(cover.Value.Data, cover.Value.ContentType);
        });

        group.MapPut("/series/{id}/thumbnail", async (
            string id,
            [FromBody] DiarSpeicherSeriesThumbnailInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var applied = await service.SetSeriesThumbnailFromMediaAsync(user, id, input.MediaId, ct);
            return applied ? Results.Ok(new { updated = true }) : Results.NotFound();
        }).RequireManageLibrary();

        group.MapPost("/series/{id}/thumbnail", HandleSeriesCoverUpload).DisableAntiforgery().RequireManageLibrary();

        group.MapDelete("/series/{id}/thumbnail", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var cleared = await service.ClearSeriesThumbnailAsync(user, id, ct);
            return cleared ? Results.NoContent() : Results.NotFound();
        }).RequireManageLibrary();

        group.MapGet("/series/{id}/media", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetSeriesMediaAsync(user, id, page, pageSize, ct);
            return Results.Ok(result);
        });
    }

    /// <summary>
    /// Portada personalizada. Una imagen suelta cabe de sobra en el limite por defecto de
    /// Kestrel, asi que aqui si vale leer el formulario de una pieza.
    /// </summary>
    private static async Task<IResult> HandleSeriesCoverUpload(
        string id,
        HttpContext httpContext,
        [FromServices] IDiarSpeicherService service,
        CancellationToken ct)
    {
        var user = (AuthUser)httpContext.Items[AuthUserKey]!;
        if (!httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Se espera multipart/form-data con la imagen." });
        }

        var form = await httpContext.Request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "No llego ninguna imagen." });
        }

        await using var stream = file.OpenReadStream();
        var applied = await service.SetSeriesThumbnailAsync(user, id, stream, Path.GetFileName(file.FileName), ct);

        return applied
            ? Results.Ok(new { updated = true })
            : Results.BadRequest(new { error = "Solo se aceptan imagenes JPG, PNG o WebP." });
    }

    private static void MapLibraryRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/libraries", async (
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetLibrariesAsync(user, ct);
            return Results.Ok(result);
        });

        group.MapGet("/libraries/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetLibraryByIdAsync(user, id, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/libraries", async (
            [FromBody] DiarSpeicherCreateLibraryInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            try
            {
                var result = await service.CreateLibraryAsync(user, input, ct);
                return result == null
                    ? Results.BadRequest(new { error = "Could not create library" })
                    : Results.Created($"/api/v2/libraries/{result.Id}", result);
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        }).RequireManageLibrary();

        group.MapPut("/libraries/{id}", async (
            string id,
            [FromBody] DiarSpeicherUpdateLibraryInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            try
            {
                var result = await service.UpdateLibraryAsync(user, id, input, ct);
                return result == null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        }).RequireManageLibrary();

        group.MapGet("/libraries/{id}/missing", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;

            return Results.Ok(await service.GetMissingAsync(user, id, ct));
        });

        group.MapPost("/libraries/{id}/purge-missing", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            return Results.Ok(new { removed = await service.PurgeMissingAsync(user, id, ct) });
        }).RequireManageLibrary();

        group.MapGet("/libraries/{id}/scan", (
            string id,
            [FromServices] IScanProgressHub hub) =>
        {
            var snapshot = hub.GetSnapshot(id);

            return snapshot == null ? Results.NoContent() : Results.Ok(ToScanDto(snapshot));
        });

        // SSE y no websocket: el progreso solo baja del servidor, asi que un canal HTTP de
        // toda la vida basta y atraviesa el gateway sin configurarle nada.
        group.MapGet("/libraries/{id}/scan/stream", async (
            string id,
            HttpContext httpContext,
            [FromServices] IScanProgressHub hub,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache, no-transform";
            // Sin esto nginx acumula la respuesta y el cliente no recibe nada hasta el final.
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            using var heartbeat = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, heartbeat.Token);
            var pinger = SendHeartbeatsAsync(httpContext.Response, linked.Token);

            try
            {
                await foreach (var snapshot in hub.SubscribeAsync(id, linked.Token))
                {
                    var payload = JsonSerializer.Serialize(ToScanDto(snapshot), ScanJson);
                    await httpContext.Response.WriteAsync($"data: {payload}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);

                    if (snapshot.Finished) break;
                }
            }
            finally
            {
                await heartbeat.CancelAsync();
                await pinger;
            }
        });

        group.MapGet("/libraries/scans", (
            [FromServices] IScanProgressHub hub) =>
            Results.Ok(hub.GetActive().Select(ToScanDto).ToList()));

        group.MapDelete("/libraries/{id}", async (
            string id,
            [FromQuery] bool deleteFiles,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            try
            {
                return await service.DeleteLibraryAsync(user, id, deleteFiles, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        }).RequireManageLibrary();

        group.MapDelete("/series/{id}", async (
            string id,
            [FromQuery] bool deleteFiles,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            try
            {
                return await service.DeleteSeriesAsync(user, id, deleteFiles, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        }).RequireManageLibrary();

        group.MapDelete("/media/{id}", async (
            string id,
            [FromQuery] bool deleteFile,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            try
            {
                return await service.DeleteMediaAsync(user, id, deleteFile, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        }).RequireManageLibrary();

        group.MapPost("/libraries/{id}/upload", HandleLibraryUpload).DisableAntiforgery();

        group.MapPost("/libraries/{id}/scan", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ScanLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to scan libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var success = await service.TriggerLibraryScanAsync(user, id, ct);
            return success ? Results.Accepted($"/api/v2/libraries/{id}") : Results.NotFound();
        });
    }

    private static RouteHandlerBuilder RequireManageLibrary(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var user = ctx.HttpContext.Items[AuthUserKey] as AuthUser;
            if (user is null || !user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(ctx);
        });

    private static async Task<IResult> HandleLibraryUpload(
        string id,
        HttpContext httpContext,
        [FromServices] IDiarSpeicherService service,
        [FromServices] IOptions<StorageOptions> storageOptions,
        CancellationToken ct)
    {
        var user = (AuthUser)httpContext.Items[AuthUserKey]!;
        if (!httpContext.Request.HasFormContentType)
            return Results.BadRequest("Expected multipart/form-data");

        var uploadOptions = storageOptions.Value.Upload;
        if (!uploadOptions.EnableUpload)
        {
            return Results.Json(new { error = "Uploads are disabled on this server." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var maxUploadBytes = uploadOptions.MaxRequestBytes;

        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null && !sizeFeature.IsReadOnly)
        {
            sizeFeature.MaxRequestBodySize = maxUploadBytes;
        }

        var mediaType = MediaTypeHeaderValue.Parse(httpContext.Request.ContentType);
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
        {
            return Results.BadRequest("Missing multipart boundary");
        }

        var reader = new MultipartReader(boundary, httpContext.Request.Body);

        var (formSubpath, firstFile) = await ReadUntilFirstFileAsync(reader, ct);
        var querySubpath = httpContext.Request.Query["subpath"].FirstOrDefault();
        var subpath = string.IsNullOrEmpty(querySubpath) ? formSubpath : querySubpath;

        var files = ReadMultipartFilesAsync(reader, firstFile, ct);

        var result = await service.UploadToLibraryAsync(user, id, subpath, files, ct);

        return result.Outcome switch
        {
            UploadOutcome.Success => Results.Ok(result.Response),
            UploadOutcome.UploadDisabled => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status403Forbidden),
            UploadOutcome.PermissionDenied => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status403Forbidden),
            UploadOutcome.LibraryNotFound => Results.NotFound(new { error = result.Message }),
            UploadOutcome.FileTooLarge => Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status413PayloadTooLarge),
            _ => Results.BadRequest(new { error = result.Message })
        };
    }

    private static async Task<(string? Subpath, DiarSpeicherUploadFileInput? FirstFile)> ReadUntilFirstFileAsync(
        MultipartReader reader,
        CancellationToken ct)
    {
        string? subpath = null;
        MultipartSection? section;

        while ((section = await reader.ReadNextSectionAsync(ct)) != null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) || cd == null)
            {
                continue;
            }

            if (cd.IsFormDisposition())
            {
                var name = HeaderUtilities.RemoveQuotes(cd.Name).Value;
                if (string.Equals(name, "subpath", StringComparison.OrdinalIgnoreCase))
                {
                    using var streamReader = new StreamReader(section.Body);
                    var val = await streamReader.ReadToEndAsync(ct);
                    if (!string.IsNullOrWhiteSpace(val)) subpath = val;
                }
                continue;
            }

            var file = ToFileInput(section, cd);
            if (file != null) return (subpath, file);
        }

        return (subpath, null);
    }

    private static DiarSpeicherUploadFileInput? ToFileInput(MultipartSection section, ContentDispositionHeaderValue cd)
    {
        if (!cd.IsFileDisposition()) return null;

        var rawName = HeaderUtilities.RemoveQuotes(cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value).Value;
        if (string.IsNullOrWhiteSpace(rawName)) return null;

        return new DiarSpeicherUploadFileInput
        {
            FileName = rawName,
            Content = section.Body
        };
    }

    private static async IAsyncEnumerable<DiarSpeicherUploadFileInput> ReadMultipartFilesAsync(
        MultipartReader reader,
        DiarSpeicherUploadFileInput? firstFile,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (firstFile != null) yield return firstFile;

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) != null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd) || cd == null)
            {
                continue;
            }

            var file = ToFileInput(section, cd);
            if (file != null) yield return file;
        }
    }

    private static void MapEpubRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/epub/{id}/toc", async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var toc = await service.GetEpubTocAsync(user, id, ct);
            return toc == null ? Results.NotFound() : Results.Ok(toc);
        });

        group.MapGet("/epub/{id}/resource/{*resourcePath}", async (
            string id,
            string resourcePath,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var res = await service.GetEpubResourceAsync(user, id, resourcePath, ct);
            if (res == null) return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(res.Value.Data, res.Value.ContentType);
        });
    }

    private static void MapUserProfileRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/users/{id}/epub-profiles", HandleGetEpubProfiles);
        group.MapPut("/users/{id}/epub-profiles", HandlePutEpubProfiles);
    }

    private static async Task<IResult> HandleGetEpubProfiles(
        string id,
        HttpContext httpContext,
        [FromServices] DiarSpeicherDbContext db,
        CancellationToken ct)
    {
        if (httpContext.Items[AuthUserKey] is not AuthUser user) return Results.Unauthorized();

        var targetId = string.Equals(id, "me", StringComparison.OrdinalIgnoreCase) ? user.Id : id;
        if (user.Id != targetId && !user.IsServerOwner && !user.HasRole("admin"))
        {
            return Results.Forbid();
        }

        var prefs = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == targetId, ct);
        if (string.IsNullOrWhiteSpace(prefs?.EpubProfilesJson))
        {
            return Results.Ok(EpubDeviceProfile.GetDefaults());
        }

        try
        {
            var profiles = JsonSerializer.Deserialize<List<EpubDeviceProfile>>(prefs.EpubProfilesJson);
            return Results.Ok(profiles ?? EpubDeviceProfile.GetDefaults());
        }
        catch
        {
            return Results.Ok(EpubDeviceProfile.GetDefaults());
        }
    }

    private static async Task<IResult> HandlePutEpubProfiles(
        string id,
        [FromBody] List<EpubDeviceProfile> profiles,
        HttpContext httpContext,
        [FromServices] DiarSpeicherDbContext db,
        CancellationToken ct)
    {
        if (httpContext.Items[AuthUserKey] is not AuthUser user) return Results.Unauthorized();

        var targetId = string.Equals(id, "me", StringComparison.OrdinalIgnoreCase) ? user.Id : id;
        if (user.Id != targetId && !user.IsServerOwner && !user.HasRole("admin"))
        {
            return Results.Forbid();
        }

        var newProfile = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
        var affectedBooks = 0;
        if (newProfile != null)
        {
            var profileKey = EpubRasterizer.BuildProfileKey(newProfile);
            affectedBooks = await db.ReadingSessions
                .CountAsync(s => s.UserId == targetId
                    && s.Status == ReadingStatus.Reading
                    && s.RenderedPage != null
                    && s.RenderedProfileKey != profileKey, ct);
        }

        var prefs = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == targetId, ct);
        if (prefs == null)
        {
            prefs = new UserPreferences
            {
                UserId = targetId,
                EpubProfilesJson = JsonSerializer.Serialize(profiles)
            };
            db.UserPreferences.Add(prefs);
        }
        else
        {
            prefs.EpubProfilesJson = JsonSerializer.Serialize(profiles);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { updated = true, count = profiles.Count, affectedBooks });
    }
}
