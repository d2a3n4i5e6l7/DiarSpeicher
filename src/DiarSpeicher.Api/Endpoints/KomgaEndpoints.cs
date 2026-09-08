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

        return endpoints;
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
