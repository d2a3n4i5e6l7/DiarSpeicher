using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Opds;

namespace DiarSpeicher.Api.Endpoints;

public static class OpdsEndpoints
{
    private const string AtomContentType = "application/atom+xml;profile=opds-catalog;charset=utf-8";
    private const string OpenSearchContentType = "application/opensearchdescription+xml;charset=utf-8";

    public static IEndpointRouteBuilder MapOpdsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGroup(endpoints.MapGroup("/opds/v1.2"));

        MapGroup(endpoints.MapGroup("/opds/{apiKey}/v1.2"));

        return endpoints;
    }

    private static void MapGroup(RouteGroupBuilder group)
    {
        group.MapGet("/catalog", async (HttpContext context, IOpdsService opdsService, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetCatalogXmlAsync(user, apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/search", (HttpContext context, IOpdsService opdsService) =>
        {
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = opdsService.GetOpenSearchXml(apiKey);
            return Results.Content(xml, OpenSearchContentType);
        });

        group.MapGet("/search/feed", async (
            string? search,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetSearchFeedXmlAsync(user, search, apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/keep-reading", async (HttpContext context, IOpdsService opdsService, CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetKeepReadingFeedXmlAsync(user, apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/libraries", async (
            string? search,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetLibrariesFeedAsync(user, search, apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/libraries/{id}", async (
            string id,
            int? page,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            try
            {
                var xml = await opdsService.GetLibrarySeriesFeedAsync(user, id, Math.Max(0, page ?? 0), apiKey, ct);
                return Results.Content(xml, AtomContentType);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"Library {id} not found");
            }
        });

        group.MapGet("/series", async (
            string? search,
            int? page,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetSeriesFeedAsync(user, search, Math.Max(0, page ?? 0), apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/series/latest", async (
            int? page,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetLatestSeriesFeedAsync(user, Math.Max(0, page ?? 0), apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/series/{id}", async (
            string id,
            int? page,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            try
            {
                var xml = await opdsService.GetSeriesBooksFeedAsync(user, id, Math.Max(0, page ?? 0), apiKey, ct);
                return Results.Content(xml, AtomContentType);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound($"Series {id} not found");
            }
        });

        group.MapGet("/books", async (
            string? search,
            int? page,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetBooksFeedAsync(user, search, Math.Max(0, page ?? 0), apiKey, ct);
            return Results.Content(xml, AtomContentType);
        });

        group.MapGet("/books/latest", async (
            int? page,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var apiKey = RequestIdentity.GetApiKey(context);
            var xml = await opdsService.GetLatestBooksFeedAsync(user, Math.Max(0, page ?? 0), apiKey, ct);
            return Results.Content(xml, AtomContentType);
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

        group.MapGet("/books/{id}/pages/{page:int}", async (
            string id,
            int page,
            bool? zero_based,
            bool? cover,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = RequestIdentity.GetAuthUser(context);
            var zeroBased = zero_based ?? true;

            var isCover = cover ?? false;
            var (extractedPage, book) = await opdsService.GetBookPageAsync(
                user,
                id,
                zeroBased ? page + 1 : page,
                isCover ? PageRequestKind.Cover : PageRequestKind.Reading,
                ct);

            if (book == null)
            {
                return Results.NotFound($"Book {id} not found or unauthorized");
            }

            if (extractedPage == null || extractedPage.Data.Length == 0)
            {
                return Results.NotFound($"Page {page} of book {id} not found");
            }

            return Results.File(extractedPage.Data, extractedPage.ContentType.MimeType());
        });

        group.MapGet("/books/{id}/file", async (
            string id,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            return await ServeBookFileAsync(id, context, opdsService, ct);
        });

        group.MapGet("/books/{id}/file/{filename}", async (
            string id,
            string filename,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            return await ServeBookFileAsync(id, context, opdsService, ct);
        });
    }

    private static async Task<IResult> ServeBookFileAsync(
        string id,
        HttpContext context,
        IOpdsService opdsService,
        CancellationToken ct)
    {
        var user = RequestIdentity.GetAuthUser(context);
        var book = await opdsService.GetMediaForDownloadAsync(user, id, ct);

        if (book == null || !File.Exists(book.Path))
        {
            return Results.NotFound($"Book {id} not found or unauthorized");
        }

        var fileName = Path.GetFileName(book.Path);
        var mimeType = Core.Filesystem.ContentTypeExtensions.FromExtension(book.Extension).MimeType();

        return Results.File(
            book.Path,
            contentType: mimeType,
            fileDownloadName: fileName,
            enableRangeProcessing: true);
    }

}
