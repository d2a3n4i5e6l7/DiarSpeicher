using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.Endpoints;

public static partial class FilesystemEndpoints
{
    private const string AuthUserKey = "AuthUser";

    private const int MaxEntries = 1000;

    private const string OutsideRoots = "Esa ruta esta fuera de las carpetas permitidas.";

    private const string NotDeletable =
        "Esa ruta no se puede borrar: esta fuera de las carpetas permitidas, es una raiz o es la propia papelera.";

    public static RouteGroupBuilder MapFilesystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v2/filesystem");

        MapRootRoutes(group);
        MapIndexRoutes(group);
        MapTrashRoutes(group);
        MapBrowseRoutes(group);

        return group;
    }

    private static void MapRootRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/roots", (
            HttpContext httpContext,
            [FromServices] IOptions<LibraryRootsOptions> options) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            var roots = options.Value.ResolveRoots()
                .Select(r => new FolderRootDto
                {
                    Path = r,
                    Name = Path.GetFileName(r) is { Length: > 0 } name ? name : r,
                    Mounted = Directory.Exists(r)
                })
                .ToList();

            return Results.Ok(roots);
        });
    }

    private static void MapIndexRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/index", (
            HttpContext httpContext,
            [FromServices] IFolderIndex index) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Results.Ok(index.GetStatus());
        });

        group.MapPost("/index", (
            HttpContext httpContext,
            [FromServices] IFolderIndex index) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return index.TryStartRefresh()
                ? Results.Accepted(value: index.GetStatus())
                : ApiResults.Error("Ya hay un repaso en marcha.", StatusCodes.Status409Conflict);
        });

        group.MapGet("/search", (
            [FromQuery] string? q,
            [FromQuery] int? limit,
            HttpContext httpContext,
            [FromServices] IFolderIndex index) =>
        {
            if (!IsAllowed(httpContext)) return Forbidden();

            return Results.Ok(index.Search(q ?? string.Empty, limit ?? 60));
        });
    }

    private static void MapTrashRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/deletion", HandleDeletionInspect);
        group.MapDelete("/entry", HandleDeleteEntry);
    }
}
