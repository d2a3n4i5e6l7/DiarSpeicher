using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static class KoboEndpoints
{
    private const string AuthUserKey = "AuthUser";

    public static RouteGroupBuilder MapKoboEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/kobo/{apiKey}");

        group.MapGet("/v1/initialization", async (
            string apiKey,
            HttpContext httpContext,
            [FromServices] IKoboService service,
            CancellationToken ct) =>
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}";
            var resources = await service.GetInitializationAsync(baseUrl, apiKey, ct);

            httpContext.Response.Headers["x-kobo-apitoken"] = "e30=";
            return Results.Ok(new { Resources = resources });
        });

        group.MapGet("/v1/library/sync", async (
            string apiKey,
            HttpContext httpContext,
            [FromServices] IKoboService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}";
            var clientSyncToken = httpContext.Request.Headers["x-kobo-synctoken"].FirstOrDefault();

            var syncResult = await service.SyncLibraryAsync(user, baseUrl, apiKey, clientSyncToken, 100, ct);

            httpContext.Response.Headers["x-kobo-synctoken"] = syncResult.SyncToken;
            if (syncResult.ShouldContinue)
            {
                httpContext.Response.Headers["x-kobo-sync"] = "continue";
            }

            return Results.Ok(syncResult.Items);
        });

        group.MapGet("/v1/library/{bookId}/metadata", async (
            string apiKey,
            string bookId,
            HttpContext httpContext,
            [FromServices] IKoboService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}";

            var metadata = await service.GetBookMetadataAsync(user, baseUrl, apiKey, bookId, ct);
            return metadata == null ? Results.NotFound() : Results.Ok(metadata);
        });

        group.MapGet("/v1/books/{bookId}/thumbnail/{width:int}/{height:int}/{isGreyscale}/image.jpg", async (
            HttpContext httpContext,
            [FromServices] DiarSpeicher.Infrastructure.Opds.IOpdsService opdsService,
            CancellationToken ct) =>
        {
            return await HandleThumbnailAsync(httpContext, opdsService, ct);
        });

        group.MapGet("/v1/books/{bookId}/thumbnail/{width:int}/{height:int}/{quality:int}/{isGreyscale}/image.jpg", async (
            HttpContext httpContext,
            [FromServices] DiarSpeicher.Infrastructure.Opds.IOpdsService opdsService,
            CancellationToken ct) =>
        {
            return await HandleThumbnailAsync(httpContext, opdsService, ct);
        });

        group.MapGet("/v1/books/{bookId}/file/epub", async (
            string apiKey,
            string bookId,
            HttpContext httpContext,
            [FromServices] IKoboService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var file = await service.GetBookFileAsync(user, bookId, ct);
            if (file == null)
            {
                return Results.NotFound();
            }

            return Results.File(file.Value.Path, file.Value.ContentType, enableRangeProcessing: true);
        });

        return group;
    }

    private static async Task<IResult> HandleThumbnailAsync(
        HttpContext httpContext,
        DiarSpeicher.Infrastructure.Opds.IOpdsService opdsService,
        CancellationToken ct)
    {
        var user = (AuthUser)httpContext.Items[AuthUserKey]!;
        var bookId = httpContext.GetRouteValue("bookId")?.ToString() ?? string.Empty;

        var (data, contentType) = await opdsService.GetBookThumbnailAsync(user, bookId, ct);
        if (data == null)
        {
            return Results.NotFound();
        }

        return Results.File(data, contentType);
    }
}
