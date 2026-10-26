using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static partial class MetadataEndpoints
{
    private const string AuthUserKey = "AuthUser";

    public static RouteGroupBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/metadata");

        group.MapGet("/status", (
            [FromServices] IMangaBakaIngestService ingest,
            [FromServices] IMangaBakaCatalog catalog,
            CancellationToken ct) => GetStatusAsync(ingest, catalog, ct));

        group.MapPost("/download", (
            HttpContext httpContext,
            [FromServices] IMangaBakaIngestService ingest) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return ToResult(ingest.TryStartDownload(), ingest);
        });

        group.MapPost("/import", HandleImportAsync).DisableAntiforgery();
        group.MapPost("/import/init", HandleImportInitAsync).DisableAntiforgery();
        group.MapPost("/import/chunk", HandleImportChunkAsync).DisableAntiforgery();
        group.MapPost("/import/complete", HandleImportCompleteAsync).DisableAntiforgery();

        // No management permission on purpose: it is an image, visible to anyone who can
        // already see the record. What is restricted is the origin host, in IsAllowedCover.
        group.MapGet("/cover", async (
            [FromQuery] string url,
            HttpContext httpContext,
            [FromServices] IMangaBakaIngestService ingest,
            CancellationToken ct) =>
        {
            var cover = await ingest.FetchCoverAsync(url, ct);
            if (cover is null) return Results.NotFound();

            // Dump covers do not change between refreshes: caching them for a week stops every
            // dialog opening from going back to the internet.
            httpContext.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(cover.Value.Data, cover.Value.ContentType);
        });

        group.MapGet("/series/{seriesId}/candidates", async (
            string seriesId,
            HttpContext httpContext,
            [FromServices] ISeriesMetadataMatcher matcher,
            [FromQuery] int limit = 10,
            CancellationToken ct = default) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var candidates = await matcher.SuggestAsync(seriesId, limit, ct);
            return Results.Ok(candidates);
        });

        group.MapPut("/series/{seriesId}/match/{mangaBakaId:int}", async (
            string seriesId,
            int mangaBakaId,
            HttpContext httpContext,
            [FromServices] ISeriesMetadataMatcher matcher,
            CancellationToken ct) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var applied = await matcher.ApplyAsync(seriesId, mangaBakaId, ct);
            return applied ? Results.Ok(new MatchedResponse(true)) : Results.NotFound();
        });

        group.MapDelete("/series/{seriesId}/match", async (
            string seriesId,
            HttpContext httpContext,
            [FromServices] ISeriesMetadataMatcher matcher,
            CancellationToken ct) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var cleared = await matcher.ClearAsync(seriesId, ct);
            return cleared ? Results.NoContent() : Results.NotFound();
        });

        return group;
    }

    private static async Task<IResult> GetStatusAsync(
        IMangaBakaIngestService ingest,
        IMangaBakaCatalog catalog,
        CancellationToken ct)
    {
        var status = ingest.GetStatus();
        if (catalog.IsAvailable)
        {
            status.SeriesCount = await catalog.CountAsync(ct);
        }
        return Results.Ok(status);
    }

    public sealed record MetadataImportInitRequest(string FileName, long TotalBytes);
    public sealed record MetadataImportCompleteRequest(string UploadId, string FileName);
}
