using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Opds;

namespace DiarSpeicher.Api.Endpoints;

public static class OpdsV2Endpoints
{
    public static IEndpointRouteBuilder MapOpdsV2Endpoints(this IEndpointRouteBuilder endpoints)
    {
        // Komga monta OPDS 2 en "/opds/v2/" (Opds2Controller.kt), no en "/opds/v2.0/". Un
        // cliente escrito contra Komga pide la ruta corta y aqui recibia un 404, asi que se
        // registran las dos: la larga se mantiene para no romper a quien ya la use.
        MapGroup(endpoints.MapGroup("/opds/v2.0"));
        MapGroup(endpoints.MapGroup("/opds/{apiKey}/v2.0"));
        MapGroup(endpoints.MapGroup("/opds/v2"));
        MapGroup(endpoints.MapGroup("/opds/{apiKey}/v2"));

        return endpoints;
    }

    private static void MapGroup(RouteGroupBuilder group)
    {
        MapFeedEndpoints(group);
        MapBookEndpoints(group);
    }

    private static void MapFeedEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/auth", (HttpContext context, IOpdsV2Service opdsV2) =>
        {
            var apiKey = GetApiKey(context);
            var doc = opdsV2.GetAuthenticationDoc(apiKey);
            return Results.Json(doc, contentType: OpdsV2MimeTypes.AuthenticationJson);
        });

        group.MapGet("/catalog", async (HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var feed = await opdsV2.GetCatalogFeedAsync(user, apiKey, ct);
            return Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        });

        group.MapGet("/search", async (string? query, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var feed = await opdsV2.SearchFeedAsync(user, query ?? string.Empty, apiKey, ct);
            return Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        });

        group.MapGet("/libraries", async (HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var feed = await opdsV2.GetLibrariesFeedAsync(user, apiKey, ct);
            return Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        });

        async Task<IResult> LibraryFeed(string id, int? page, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct)
        {
            var feed = await opdsV2.GetLibrarySeriesFeedAsync(GetAuthUser(context), id, Math.Max(0, page ?? 0), GetApiKey(context), ct);
            return feed == null
                ? Results.NotFound()
                : Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        }

        group.MapGet("/libraries/{id}", LibraryFeed);
        group.MapGet("/libraries/{id}/browse", LibraryFeed);

        group.MapGet("/series/{id}", async (string id, int? page, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var feed = await opdsV2.GetSeriesBooksFeedAsync(GetAuthUser(context), id, Math.Max(0, page ?? 0), GetApiKey(context), ct);
            return feed == null
                ? Results.NotFound()
                : Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        });

        group.MapGet("/series", async (int? page, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var feed = await opdsV2.GetSeriesFeedAsync(user, Math.Max(0, page ?? 0), apiKey, ct);
            return Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        });
    }

    private static void MapBookEndpoints(RouteGroupBuilder group)
    {
        // Komga cuelga estos tres de "libraries/" (Opds2Controller.kt), no de "books/". Se
        // registran las dos formas contra el mismo handler para que un cliente escrito
        // contra Komga encuentre el feed donde lo busca.
        async Task<IResult> BooksFeed(int? page, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct)
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var feed = await opdsV2.GetBooksFeedAsync(user, Math.Max(0, page ?? 0), apiKey, ct);
            return Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        }

        async Task<IResult> KeepReadingFeed(HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct)
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var feed = await opdsV2.GetKeepReadingFeedAsync(user, apiKey, ct);
            return Results.Json(feed, contentType: OpdsV2MimeTypes.OpdsJson);
        }

        group.MapGet("/books/browse", BooksFeed);
        group.MapGet("/libraries/browse", BooksFeed);

        group.MapGet("/books/latest", BooksFeed);
        group.MapGet("/libraries/books/latest", BooksFeed);

        group.MapGet("/books/keep-reading", KeepReadingFeed);
        group.MapGet("/libraries/keep-reading", KeepReadingFeed);

        group.MapGet("/books/{id}", async (string id, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var apiKey = GetApiKey(context);
            var pub = await opdsV2.GetPublicationAsync(user, id, apiKey, ct);
            return pub == null
                ? Results.NotFound($"Publication {id} not found")
                : Results.Json(pub, contentType: OpdsV2MimeTypes.PublicationJson);
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

        group.MapGet("/books/{id}/pages/{page:int}", async (
            string id,
            int page,
            bool? zero_based,
            HttpContext context,
            IOpdsService opdsService,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var zeroBased = zero_based ?? true;
            var (extractedPage, book) = await opdsService.GetBookPageAsync(user, id, page, zeroBased, trackProgression: true, ct);

            if (book == null || extractedPage == null || extractedPage.Data.Length == 0)
            {
                return Results.NotFound($"Page {page} of book {id} not found");
            }

            return Results.File(extractedPage.Data, extractedPage.ContentType.ToMimeType());
        });

        group.MapGet("/books/{id}/progression", async (string id, HttpContext context, IOpdsV2Service opdsV2, CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var prog = await opdsV2.GetProgressionAsync(user, id, ct);
            return prog == null ? Results.NotFound() : Results.Ok(prog);
        });

        group.MapPut("/books/{id}/progression", async (
            string id,
            OpdsV2Progression input,
            HttpContext context,
            IOpdsV2Service opdsV2,
            CancellationToken ct) =>
        {
            var user = GetAuthUser(context);
            var ok = await opdsV2.UpdateProgressionAsync(user, id, input, ct);
            return ok ? Results.Ok() : Results.BadRequest("Could not update progression");
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

            return Results.File(
                book.Path,
                contentType: ContentTypeExtensions.FromExtension(book.Extension).MimeType(),
                fileDownloadName: Path.GetFileName(book.Path),
                enableRangeProcessing: true);
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

    private static string? GetApiKey(HttpContext context)
    {
        if (context.Items.TryGetValue("OpdsApiKey", out var obj) && obj is string apiKey)
        {
            return apiKey;
        }

        if (context.Request.RouteValues.TryGetValue("apiKey", out var rVal) && rVal != null)
        {
            return rVal.ToString();
        }

        return null;
    }
}
