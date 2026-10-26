using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;
using DiarSpeicher.Core.Domain.Entities;

namespace DiarSpeicher.Api.Endpoints;

public static partial class DiarSpeicherEndpoints
{
    private const string AuthUserKey = "AuthUser";
    private const string CacheControlPublicOneDay = "public, max-age=86400";
    private const string SeriesThumbnailRoute = "/series/{id}/thumbnail";

    /// <summary>
    /// A line starting with ":" is an SSE comment: EventSource discards it, and it is enough
    /// for nginx not to treat the connection as dead under its proxy_read_timeout.
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
            // The client left or the stream closed: there is nothing to salvage.
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
        PendingMedia = snapshot.Progress.PendingMedia,
        CurrentFile = snapshot.Progress.CurrentFile,
        ReadingFiles = snapshot.Progress.ReadingFiles,
        CreatedMedia = snapshot.Progress.CreatedMedia,
        CreatedSeries = snapshot.Progress.CreatedSeries,
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
        group.MapGet("/health", () => Results.Ok(new HealthResponse("healthy")));
        group.MapGet("/version", () => Results.Ok(new VersionResponse("0.1.0", "net11")));
        group.MapGet("/claim", async (
            [FromServices] IDiarSpeicherService service,
            CancellationToken ct) =>
        {
            var status = await service.GetSystemStatusAsync(ct);
            return Results.Ok(new ClaimStatusResponse(status.IsClaimed));
        });

        group.MapGet("/auth/me", (HttpContext httpContext) =>
        {
            var user = httpContext.Items[AuthUserKey] as AuthUser;
            if (user == null) return Results.Unauthorized();

            return Results.Ok(new CurrentUserResponse(user.Id, user.Username, user.IsServerOwner, user.AgeRestriction));
        });
    }
}
