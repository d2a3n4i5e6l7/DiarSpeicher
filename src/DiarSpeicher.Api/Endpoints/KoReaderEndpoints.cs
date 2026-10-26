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
            return ApiResults.Error("This account is not allowed to use KOReader sync.", StatusCodes.Status403Forbidden);
        }

        return await next(ctx);
    }

    public static RouteGroupBuilder MapKoReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/koreader/{apiKey}");

        // Covers /users/auth and /users/create too: without the permission the KOReader client
        // has to find out while being configured, not on its first sync attempt.
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
