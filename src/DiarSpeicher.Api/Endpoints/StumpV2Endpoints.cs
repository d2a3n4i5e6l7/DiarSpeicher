using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.StumpV2;
using Microsoft.AspNetCore.Mvc;

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
            var result = await service.CreateLibraryAsync(user, input, ct);
            return result == null ? Results.BadRequest("Could not create library") : Results.Created($"/api/v2/libraries/{result.Id}", result);
        });

        group.MapPost("/libraries/{id}/upload", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            if (!httpContext.Request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await httpContext.Request.ReadFormAsync(ct);
            var files = form.Files;
            if (files.Count == 0)
                return Results.BadRequest("No files provided");

            var subpath = form["subpath"].ToString();
            var uploadInputs = files.Select(f => new StumpUploadFileInput
            {
                FileName = f.FileName,
                Content = f.OpenReadStream()
            }).ToList();

            var result = await service.UploadToLibraryAsync(user, id, subpath, uploadInputs, ct);
            return result == null ? Results.BadRequest("Upload failed or invalid files") : Results.Ok(result);
        }).DisableAntiforgery();

        group.MapPost("/libraries/{id}/scan", async (
            string id,
            HttpContext httpContext,
            [FromServices] IStumpV2Service service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var success = await service.TriggerLibraryScanAsync(user, id, ct);
            return success ? Results.Accepted($"/api/v2/libraries/{id}") : Results.NotFound();
        });
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
