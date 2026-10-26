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
    /// What a Komga client requests right after connecting, before touching the catalog. Without
    /// it the server answers 404, the client deems the connection invalid and never requests
    /// libraries, however correct the catalog routes are.
    ///
    /// Komga hangs users under <c>api/v2/users</c> (UserController.kt) even though the rest of its
    /// API stays on v1, hence this registration lives outside the group.
    /// </summary>
    private static void MapSessionEndpoints(IEndpointRouteBuilder endpoints, RouteGroupBuilder group)
    {
        endpoints.MapGet("/api/v2/users/me", (HttpContext context) =>
        {
            var user = RequestIdentity.GetAuthUser(context);

            // Komga identifies users by e-mail; there are no e-mails here, so the user name
            // goes instead, which is what the client ends up displaying.
            var roles = new List<string> { "USER" };
            if (user.IsServerOwner || user.HasRole("admin"))
            {
                roles.Add("ADMIN");
            }

            var restriction = user.RestrictOnUnset ? "ALLOW_ONLY" : "EXCLUDE";
            var ageRestriction = user.AgeRestriction.HasValue
                ? new KomgaAgeRestrictionResponse(user.AgeRestriction.Value, restriction)
                : null;

            return Results.Ok(new KomgaCurrentUserResponse(
                user.Id,
                user.Username,
                roles,
                user.ExcludedLibraryIds.Count == 0,
                [],
                [],
                [],
                ageRestriction));
        });

        // The server always has an owner: ownership goes to the first user who arrives, so
        // there is never anything for a client to claim.
        group.MapGet("/claim", () => Results.Ok(new ClaimStatusResponse(true)));

        // In Komga this turns a header session into a cookie. Here the Gateway manages the
        // session and there is nothing to write, but the client expects the 204.
        group.MapGet("/login/set-cookie", () => Results.NoContent());
    }

    private static void MapLibraryEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/libraries", async (HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var libs = await komga.GetLibrariesAsync(user, ct);
            return Results.Ok(libs);
        });

        group.MapGet("/libraries/{id}", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
            var result = await komga.GetSeriesAsync(user, library_id, search, Math.Max(0, page ?? 0), Math.Clamp(size ?? 20, 1, 100), ct);
            return Results.Ok(result);
        });

        group.MapGet("/series/{id}", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
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
        MapBookListRoutes(group);
        MapBookContentRoutes(group);
    }

    private static void MapBookListRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/books", async (
            string? search,
            int? page,
            int? size,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
            var result = await komga.GetLatestBooksAsync(user, Math.Max(0, page ?? 0), Math.Clamp(size ?? 20, 1, 100), ct);
            return Results.Ok(result);
        });

        group.MapGet("/books/{id}", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var book = await komga.GetBookByIdAsync(user, id, ct);
            return book == null ? Results.NotFound() : Results.Ok(book);
        });
    }

    private static void MapBookContentRoutes(RouteGroupBuilder group)
    {
        group.MapGet("/books/{id}/pages", async (string id, HttpContext context, IKomgaService komga, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
            var (extractedPage, book) = await opdsService.OpenBookPageAsync(user, id, pageNumber, PageRequestKind.Reading, ct);

            if (book == null || extractedPage == null)
            {
                return Results.NotFound($"Page {pageNumber} of book {id} not found");
            }

            return Results.File(extractedPage.Content, extractedPage.ContentType.ToMimeType());
        });

        group.MapGet("/books/{id}/thumbnail", async (
            string id,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
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
            var user = RequestIdentity.GetAuthUser(context);
            var ok = await komga.UpdateReadProgressAsync(user, id, dto.Page, dto.Completed, ct);
            return ok ? Results.NoContent() : Results.BadRequest("Could not update read progress");
        });

        group.MapDelete("/books/{id}/read-progress", async (
            string id,
            HttpContext context,
            IKomgaService komga,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var ok = await komga.DeleteReadProgressAsync(user, id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });
    }

}
