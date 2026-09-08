using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.AspNetCore.Mvc;

namespace DiarSpeicher.Api.Endpoints;

public static class KoReaderEndpoints
{
    public static RouteGroupBuilder MapKoReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/koreader/{apiKey}");

        group.MapGet("/users/auth", async (
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
