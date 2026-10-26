using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static partial class DiarSpeicherEndpoints
{
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

        // Anyone who can see the series can see its cover; changing it requires library
        // management.
        group.MapGet(SeriesThumbnailRoute, async (
            string id,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var cover = await service.GetSeriesThumbnailAsync(user, id, ct);
            if (cover is null) return Results.NotFound();

            // No revalidation: the URL carries a token that changes whenever the cover changes.
            httpContext.Response.Headers.CacheControl = CacheControlPublicOneDay;
            return Results.File(cover.Value.Data, cover.Value.ContentType);
        });

        group.MapPut(SeriesThumbnailRoute, async (
            string id,
            [FromBody] DiarSpeicherSeriesThumbnailInput input,
            HttpContext httpContext,
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items[AuthUserKey]!;
            var applied = await service.SetSeriesThumbnailFromMediaAsync(user, id, input.MediaId, ct);
            return applied ? Results.Ok(new UpdatedResponse(true)) : Results.NotFound();
        }).RequireManageLibrary();

        group.MapPost(SeriesThumbnailRoute, HandleSeriesCoverUpload).DisableAntiforgery().RequireManageLibrary();

        group.MapDelete(SeriesThumbnailRoute, async (
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
    /// A single image fits comfortably within Kestrel's default limit, so reading the whole
    /// form at once is fine here.
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
            return Results.BadRequest(new ApiError("Se espera multipart/form-data con la imagen."));
        }

        var form = await httpContext.Request.ReadFormAsync(ct);
        var file = form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new ApiError("No llego ninguna imagen."));
        }

        await using var stream = file.OpenReadStream();
        var applied = await service.SetSeriesThumbnailAsync(user, id, stream, Path.GetFileName(file.FileName), ct);

        return applied
            ? Results.Ok(new UpdatedResponse(true))
            : Results.BadRequest(new ApiError("Solo se aceptan imagenes JPG, PNG o WebP."));
    }
}
