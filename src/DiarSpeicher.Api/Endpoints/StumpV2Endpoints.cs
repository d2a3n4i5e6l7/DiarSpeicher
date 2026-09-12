using System.Runtime.CompilerServices;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.StumpV2;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DiarSpeicher.Api.Endpoints;

public static class StumpV2Endpoints
{
    private const string AuthUserKey = "AuthUser";

    public static RouteGroupBuilder MapStumpV2Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2");

        MapSystemRoutes(group);
        MapMediaRoutes(group);
        MapSeriesRoutes(group);
        MapLibraryRoutes(group);
        MapEpubRoutes(group);

        return group;
    }

    private static void MapSystemRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/ping", () => Results.Ok("pong"));
        group.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        group.MapGet("/version", () => Results.Ok(new { semver = "0.1.0", rev = "net10" }));
        group.MapGet("/claim", async (
            [FromServices] IStumpV2Service service,
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
            [FromServices] IStumpV2Service service,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetMediaAsync(user, page, pageSize, ct);
            return Results.Ok(result);
        });

        group.MapGet("/media/keep-reading", async (
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetKeepReadingAsync(user, ct);
            return Results.Ok(result);
        });

        group.MapGet("/media/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
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
            [FromServices] IStumpV2Service service,
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
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var file = await service.GetMediaFileAsync(user, id, ct);
            if (file == null) return Results.NotFound();

            return Results.File(file.Value.Path, file.Value.ContentType, enableRangeProcessing: true);
        });

        group.MapPut("/media/{id}/progress", async (
            string id,
            [FromBody] StumpUpdateProgressInput input,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
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
            [FromServices] IStumpV2Service service,
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
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetSeriesByIdAsync(user, id, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPut("/series/{id}", async (
            string id,
            [FromBody] StumpUpdateSeriesInput input,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await service.UpdateSeriesAsync(user, id, input, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        // La portada la ve cualquiera que ya puede ver la serie; cambiarla es gestion.
        group.MapGet("/series/{id}/thumbnail", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
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
            [FromBody] StumpSeriesThumbnailInput input,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var applied = await service.SetSeriesThumbnailFromMediaAsync(user, id, input.MediaId, ct);
            return applied ? Results.Ok(new { updated = true }) : Results.NotFound();
        });

        group.MapPost("/series/{id}/thumbnail", HandleSeriesCoverUpload).DisableAntiforgery();

        group.MapDelete("/series/{id}/thumbnail", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var cleared = await service.ClearSeriesThumbnailAsync(user, id, ct);
            return cleared ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/series/{id}/media", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
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
        [FromServices] IStumpV2Service service,
        CancellationToken ct)
    {
        var user = (AuthUser)httpContext.Items[AuthUserKey]!;
        if (!user.HasPermission(Permissions.ManageLibrary))
        {
            return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
        }

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
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetLibrariesAsync(user, ct);
            return Results.Ok(result);
        });

        group.MapGet("/libraries/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var result = await service.GetLibraryByIdAsync(user, id, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/libraries", async (
            [FromBody] StumpCreateLibraryInput input,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await service.CreateLibraryAsync(user, input, ct);
            return result == null ? Results.BadRequest("Could not create library") : Results.Created($"/api/v2/libraries/{result.Id}", result);
        });

        group.MapPut("/libraries/{id}", async (
            string id,
            [FromBody] StumpUpdateLibraryInput input,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await service.UpdateLibraryAsync(user, id, input, ct);
            return result == null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapDelete("/libraries/{id}", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!user.HasPermission(Permissions.ManageLibrary))
            {
                return Results.Json(new { error = "This account is not allowed to manage libraries." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var deleted = await service.DeleteLibraryAsync(user, id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/libraries/{id}/upload", HandleLibraryUpload).DisableAntiforgery();

        group.MapPost("/libraries/{id}/scan", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
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

    private static async Task<IResult> HandleLibraryUpload(
        string id,
        HttpContext httpContext,
        [FromServices] IStumpV2Service service,
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
        string? subpath = httpContext.Request.Query["subpath"].FirstOrDefault();

        var files = ReadMultipartFilesAsync(reader, s => { if (string.IsNullOrEmpty(subpath)) subpath = s; }, ct);

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

    private static async IAsyncEnumerable<StumpUploadFileInput> ReadMultipartFilesAsync(
        MultipartReader reader,
        Action<string> onSubpathFound,
        [EnumeratorCancellation] CancellationToken ct)
    {
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
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        onSubpathFound(val);
                    }
                }
                continue;
            }

            if (cd.IsFileDisposition())
            {
                var rawName = HeaderUtilities.RemoveQuotes(cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value).Value;
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                yield return new StumpUploadFileInput
                {
                    FileName = rawName,
                    Content = section.Body
                };
            }
        }
    }

    private static void MapEpubRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/epub/{id}/toc", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
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
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var res = await service.GetEpubResourceAsync(user, id, resourcePath, ct);
            if (res == null) return Results.NotFound();

            httpContext.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(res.Value.Data, res.Value.ContentType);
        });
    }
}
