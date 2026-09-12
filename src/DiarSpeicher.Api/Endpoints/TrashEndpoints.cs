using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

/// <summary>
/// Lo borrado que todavia se puede recuperar. Pasado el plazo se vacia solo y estos
/// endpoints dejan de verlo.
/// </summary>
public static class TrashEndpoints
{
    private const string AuthUserKey = "AuthUser";

    public static RouteGroupBuilder MapTrashEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/trash");

        group.MapGet("/", (
            HttpContext httpContext,
            [FromServices] ITrashService trash,
            [FromServices] IOptions<TrashOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Results.Ok(trash.List().Select(e => FilesystemEndpoints.ToTrashDto(e, options.Value)).ToList());
        });

        group.MapPost("/{id}/restore", (
            string id,
            HttpContext httpContext,
            [FromServices] ITrashService trash) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return trash.Restore(id)
                ? Results.NoContent()
                : Results.BadRequest(new { error = "No se pudo restaurar: caduco, o ya existe algo con ese nombre." });
        });

        return group;
    }

    private static bool IsAllowed(HttpContext httpContext) =>
        httpContext.Items[AuthUserKey] is AuthUser user && user.HasPermission(Permissions.ManageLibrary);

    private static IResult Forbidden() =>
        Results.Json(new { error = "This account is not allowed to manage libraries." },
            statusCode: StatusCodes.Status403Forbidden);
}
