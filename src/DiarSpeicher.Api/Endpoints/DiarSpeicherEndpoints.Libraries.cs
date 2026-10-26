using System.Text.Json;
using DiarSpeicher.Api.Serialization;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static partial class DiarSpeicherEndpoints
{
    private static void MapLibraryRoutes(RouteGroupBuilder group)
    {
        MapLibraryCrudRoutes(group);
        MapLibraryMaintenanceRoutes(group);
        MapScanRoutes(group);
        MapDeletionRoutes(group);
        MapLibraryActionRoutes(group);
    }

    private static void MapLibraryCrudRoutes(RouteGroupBuilder group)
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
                    ? Results.BadRequest(new ApiError("Could not create library"))
                    : Results.Created($"/api/v2/libraries/{result.Id}", result);
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new ApiError(e.Message));
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
                return Results.BadRequest(new ApiError(e.Message));
            }
        }).RequireManageLibrary();
    }

    private static void MapLibraryMaintenanceRoutes(RouteGroupBuilder group)
    {
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
            return Results.Ok(new RemovedResponse(await service.PurgeMissingAsync(user, id, ct)));
        }).RequireManageLibrary();
    }

    private static void MapScanRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/libraries/{id}/scan", (
            string id,
            [FromServices] IScanProgressHub hub) =>
        {
            var snapshot = hub.GetSnapshot(id);

            return snapshot == null ? Results.NoContent() : Results.Ok(ToScanDto(snapshot));
        });

        // SSE rather than WebSocket: progress only flows from the server, so a plain HTTP
        // stream is enough and passes through the gateway without extra configuration.
        group.MapGet("/libraries/{id}/scan/stream", async (
            string id,
            HttpContext httpContext,
            [FromServices] IScanProgressHub hub,
            CancellationToken ct) =>
        {
            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache, no-transform";
            // Without this nginx buffers the response and the client receives nothing until the
            // end.
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            using var heartbeat = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, heartbeat.Token);
            var pinger = SendHeartbeatsAsync(httpContext.Response, linked.Token);

            try
            {
                await foreach (var snapshot in hub.SubscribeAsync(id, linked.Token))
                {
                    var payload = JsonSerializer.Serialize(ToScanDto(snapshot), ScanEventJsonContext.Default.DiarSpeicherScanStatusDto);
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
    }

    private static void MapDeletionRoutes(RouteGroupBuilder group)
    {
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
                return Results.BadRequest(new ApiError(e.Message));
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
                return Results.BadRequest(new ApiError(e.Message));
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
                return Results.BadRequest(new ApiError(e.Message));
            }
        }).RequireManageLibrary();
    }

    private static void MapLibraryActionRoutes(RouteGroupBuilder group)
    {
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
                return ApiResults.Error("This account is not allowed to scan libraries.", StatusCodes.Status403Forbidden);
            }

            var success = await service.TriggerLibraryScanAsync(user, id, ct);
            return success ? Results.Accepted($"/api/v2/libraries/{id}") : Results.NotFound();
        });
    }

    private static void RequireManageLibrary(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var user = ctx.HttpContext.Items[AuthUserKey] as AuthUser;
            if (user is null || !user.HasPermission(Permissions.ManageLibrary))
            {
                return ApiResults.Error("This account is not allowed to manage libraries.", StatusCodes.Status403Forbidden);
            }

            return await next(ctx);
        });
}
