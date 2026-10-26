using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static partial class DiarSpeicherEndpoints
{
    private static void MapMediaRoutes(RouteGroupBuilder group)
    {
        MapMediaListRoutes(group);
        MapMediaContentRoutes(group);
        MapMediaProgressRoutes(group);
    }

    private static void MapMediaListRoutes(RouteGroupBuilder group)
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
    }

    private static void MapMediaContentRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/media/{id}/page/{page:int}", async (
            string id,
            int page,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var opened = await service.OpenMediaPageAsync(user, id, page, ct);
            if (opened == null) return Results.NotFound();

            httpContext.Response.Headers.CacheControl = CacheControlPublicOneDay;
            return Results.File(opened.Content, opened.ContentType.ToMimeType());
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

            httpContext.Response.Headers.CacheControl = CacheControlPublicOneDay;
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
    }

    private static void MapMediaProgressRoutes(RouteGroupBuilder group)
    {
        group.MapPut("/media/{id}/progress", async (
            string id,
            [FromBody] DiarSpeicherUpdateProgressInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var success = await service.UpdateProgressAsync(user, id, input, ct);
            return success ? Results.Ok(new UpdatedResponse(true)) : Results.NotFound();
        });
    }
}
