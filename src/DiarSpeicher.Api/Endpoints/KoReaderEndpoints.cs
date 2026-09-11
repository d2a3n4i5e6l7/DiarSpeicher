using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static class KoReaderEndpoints
{
    private static async ValueTask<object?> RequireKoreaderSyncAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        if (ctx.HttpContext.Items["AuthUser"] is not AuthUser user ||
            !user.HasPermission(Permissions.AccessKoreaderSync))
        {
            return Results.Json(
                new { error = "This account is not allowed to use KOReader sync." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(ctx);
    }

    public static RouteGroupBuilder MapKoReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/koreader/{apiKey}");

        // Incluye /users/auth y /users/create: si el permiso no está, el cliente de KOReader
        // debe enterarse al configurarse y no al primer intento de sincronizar.
        group.AddEndpointFilter(RequireKoreaderSyncAsync);

        group.MapGet("/users/auth", async (
            [FromServices] IKoReaderService service,
            CancellationToken ct) =>
        {
            var auth = await service.CheckAuthorizedAsync(ct);
            return Results.Ok(auth);
        });

        group.MapGet("/users/create", async (
            [FromServices] IKoReaderService service,
            CancellationToken ct) =>
        {
            var auth = await service.CheckAuthorizedAsync(ct);
            return Results.Ok(auth);
        });

        group.MapGet("/syncs/progress/{document}", async (
            string apiKey,
            string document,
            HttpContext httpContext,
            [FromServices] IKoReaderService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items["AuthUser"]!;
            var progress = await service.GetProgressAsync(user, document, ct);
            return Results.Ok(progress);
        });

        group.MapPut("/syncs/progress", async (
            string apiKey,
            [FromBody] KoReaderProgressInput input,
            HttpContext httpContext,
            [FromServices] IKoReaderService service,
            CancellationToken ct) =>
        {
            var user = (AuthUser)httpContext.Items["AuthUser"]!;
            try
            {
                var result = await service.UpdateProgressAsync(user, input, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        return group;
    }
}
