using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Komga;
using DiarSpeicher.Infrastructure.Opds;

namespace DiarSpeicher.Api.Endpoints;

public static class KomgaEndpoints
{
    public static IEndpointRouteBuilder MapKomgaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1");

        MapLibraryEndpoints(group);
        MapSeriesEndpoints(group);
        MapBookEndpoints(group);
        MapProgressEndpoints(group);
        MapSessionEndpoints(endpoints, group);

        return endpoints;
    }

    /// <summary>
    /// Lo que un cliente de Komga pide nada más conectar, antes de tocar el catálogo. Sin
    /// esto responde 404, el cliente da la conexión por inválida y nunca llega a pedir
    /// bibliotecas: las rutas del catálogo pueden estar perfectas y aun así no entra.
    ///
    /// Komga colgó los usuarios de <c>api/v2/users</c> (UserController.kt) aunque el resto
    /// de su API siga en v1, de ahí que este registro vaya fuera del grupo.
    /// </summary>
    private static void MapSessionEndpoints(IEndpointRouteBuilder endpoints, RouteGroupBuilder group)
    {
        endpoints.MapGet("/api/v2/users/me", (HttpContext context) =>
        {
            var user = GetAuthUser(context);

            // Komga identifica al usuario por su correo; aquí no hay correos, así que va el
            // nombre, que es lo que el cliente acaba mostrando.
            var roles = new List<string> { "USER" };
            if (user.IsServerOwner || user.HasRole("admin"))
            {
                roles.Add("ADMIN");
            }

            // Declarado como object? y no en linea: un ternario entre un tipo anonimo y null
            // no tiene tipo comun y no compila.
            object? ageRestriction = user.AgeRestriction.HasValue
                ? new { age = user.AgeRestriction.Value, restriction = user.RestrictOnUnset ? "ALLOW_ONLY" : "EXCLUDE" }
                : null;

            return Results.Ok(new
            {
                id = user.Id,
                email = user.Username,
                roles,
                sharedAllLibraries = user.ExcludedLibraryIds.Count == 0,
                sharedLibrariesIds = Array.Empty<string>(),
                labelsAllow = Array.Empty<string>(),
                labelsExclude = Array.Empty<string>(),
                ageRestriction
            });
        });

        // El servidor siempre tiene dueño: la propiedad se concede al primer usuario que
        // llega, así que nunca hay nada que reclamar desde un cliente.
        group.MapGet("/claim", () => Results.Ok(new { isClaimed = true }));

        // En Komga convierte una sesión de cabecera en cookie. Aquí la sesión la administra
        // el Gateway y no hay nada que escribir, pero el 204 es lo que el cliente espera.
        group.MapGet("/login/set-cookie", () => Results.NoContent());
    }

    private static void MapLibraryEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/libraries", async (HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var libs = await komga.GetLibrariesAsync(user, ct);
            return Results.Ok(libs);
        });

        group.MapGet("/libraries/{id}", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var lib = await komga.GetLibraryByIdAsync(user, id, ct);
            return lib == null ? Results.NotFound() : Results.Ok(lib);
        });
    }

    private static void MapSeriesEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/series", async (
            string? library_id,
            string? search,
            int? page,
            int? size,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var result = await komga.GetSeriesAsync(user, library_id, search, Math.Max(0, page ?? 0), Math.Clamp(size ?? 20, 1, 100), ct);
            return Results.Ok(result);
        });

        group.MapGet("/series/{id}", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var series = await komga.GetSeriesByIdAsync(user, id, ct);
            return series == null ? Results.NotFound() : Results.Ok(series);
        });

        group.MapGet("/series/{id}/books", async (
            string id,
            int? page,
            int? size,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var result = await komga.GetBooksInSeriesAsync(user, id, Math.Max(0, page ?? 0), Math.Clamp(size ?? 20, 1, 100), ct);
            return Results.Ok(result);
        });

        group.MapGet("/series/{id}/thumbnail", async (
            string id,
            HttpContext context,
            IKomgaService komga,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var books = await komga.GetBooksInSeriesAsync(user, id, 0, 1, ct);
            if (books.Content.Count == 0)
            {
                return Results.NotFound($"No books found for series {id}");
            }

            var (data, contentType) = await opdsService.GetBookThumbnailAsync(user, books.Content[0].Id, ct);
            if (data == null || data.Length == 0)
            {
                return Results.NotFound($"Thumbnail for series {id} not found");
            }
            return Results.File(data, contentType);
        });
    }

    private static void MapBookEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/books", async (
            string? search,
            int? page,
            int? size,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var result = await komga.GetBooksAsync(user, search, Math.Max(0, page ?? 0), Math.Clamp(size ?? 20, 1, 100), ct);
            return Results.Ok(result);
        });

        group.MapGet("/books/latest", async (
            int? page,
            int? size,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var result = await komga.GetLatestBooksAsync(user, Math.Max(0, page ?? 0), Math.Clamp(size ?? 20, 1, 100), ct);
            return Results.Ok(result);
        });

        group.MapGet("/books/{id}", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var book = await komga.GetBookByIdAsync(user, id, ct);
            return book == null ? Results.NotFound() : Results.Ok(book);
        });

        group.MapGet("/books/{id}/pages", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var pages = await komga.GetBookPagesAsync(user, id, ct);
            return Results.Ok(pages);
        });

        group.MapGet("/books/{id}/pages/{pageNumber:int}", async (
            string id,
            int pageNumber,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var (extractedPage, book) = await opdsService.GetBookPageAsync(user, id, pageNumber, zeroBased: false, trackProgression: true, ct);

            if (book == null || extractedPage == null || extractedPage.Data.Length == 0)
            {
                return Results.NotFound($"Page {pageNumber} of book {id} not found");
            }

            return Results.File(extractedPage.Data, extractedPage.ContentType.ToMimeType());
        });

        group.MapGet("/books/{id}/thumbnail", async (
            string id,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var (data, contentType) = await opdsService.GetBookThumbnailAsync(user, id, ct);
            if (data == null || data.Length == 0)
            {
                return Results.NotFound($"Thumbnail for book {id} not found");
            }
            return Results.File(data, contentType);
        });

        group.MapGet("/books/{id}/file", async (
            string id,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var book = await opdsService.GetMediaForDownloadAsync(user, id, ct);
            if (book == null || !File.Exists(book.Path))
            {
                return Results.NotFound($"Book {id} not found or unauthorized");
            }

            var fileName = Path.GetFileName(book.Path);
            var ext = ContentTypeExtensions.FromExtension(book.Extension);

            return Results.File(
                book.Path,
                contentType: ext.ToMimeType(),
                fileDownloadName: fileName,
                enableRangeProcessing: true);
        });
    }

    private static void MapProgressEndpoints(RouteGroupBuilder group)
    {
        group.MapPatch("/books/{id}/read-progress", async (
            string id,
            KomgaReadProgressUpdateDto dto,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var ok = await komga.UpdateReadProgressAsync(user, id, dto.Page, dto.Completed, ct);
            return ok ? Results.NoContent() : Results.BadRequest("Could not update read progress");
        });

        group.MapDelete("/books/{id}/read-progress", async (
            string id,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var ok = await komga.DeleteReadProgressAsync(user, id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });
    }

    private static AuthUser GetAuthUser(HttpContext context)
    {
        if (context.Items.TryGetValue("AuthUser", out var obj) && obj is AuthUser authUser)
        {
            return authUser;
        }

        return new AuthUser { Id = "anonymous", Username = "anonymous" };
    }
}
