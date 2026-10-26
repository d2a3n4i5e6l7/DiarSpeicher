using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public partial class OpdsV2Service
{
    public async Task<OpdsV2Feed?> GetSeriesBooksFeedAsync(AuthUser user, string seriesId, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var sParams = CatalogSqlFilters.ArgsFor(user);
        sParams.SeriesId = seriesId;

        const string seriesSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status",
                   s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @SeriesId AND {CatalogSqlFilters.SeriesVisible}
            LIMIT 1;
            """;

        var seriesRows = await conn.QueryAsync<Series>(seriesSql, sParams).WithMetadataAsync(conn);

        var series = seriesRows.FirstOrDefault();
        if (series == null)
        {
            return null;
        }

        var mParams = CatalogSqlFilters.ArgsFor(user);
        mParams.SeriesId = seriesId;

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND m."SeriesId" = @SeriesId;
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, mParams);

        const string pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND m."SeriesId" = @SeriesId
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        mParams.Take = PageSize;
        mParams.Skip = page * PageSize;

        var books = (await conn.QueryAsync<Media>(pagedSql, mParams).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var epubTotals = await _progress.EpubTotalsAsync(books, ct);

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = series.Metadata?.Title ?? series.Name,
                NumberOfItems = totalCount,
                ItemsPerPage = PageSize,
                CurrentPage = page
            },
            Links = BuildPagingLinks($"series/{seriesId}", page, totalCount, apiKey),
            Publications = books.Select(b => ToPublication(b, apiKey, epubTotals.TotalFor(b.Id))).ToList()
        };
    }

    private List<OpdsV2Link> BuildPagingLinks(string endpoint, int page, int totalCount, string? apiKey)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        var links = new List<OpdsV2Link>
        {
            new(FormatUrl($"{endpoint}?page={page}", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
            new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
        };

        if (page > 0)
        {
            links.Add(new(FormatUrl($"{endpoint}?page={page - 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "previous"));
        }

        if (page < totalPages - 1)
        {
            links.Add(new(FormatUrl($"{endpoint}?page={page + 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "next"));
        }

        return links;
    }

    public async Task<OpdsV2Feed> GetBooksFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible}
            ORDER BY m."Id" DESC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = PageSize;
        parameters.Skip = page * PageSize;

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var epubTotals = await _progress.EpubTotalsAsync(books, ct);

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "Books",
                NumberOfItems = totalCount,
                ItemsPerPage = PageSize,
                CurrentPage = page
            },
            Links = BuildPagingLinks("books/browse", page, totalCount, apiKey),
            Publications = books.Select(b => ToPublication(b, apiKey, epubTotals.TotalFor(b.Id))).ToList()
        };
    }

    public async Task<OpdsV2Feed> GetKeepReadingFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var sessions = await conn.GetKeepReadingSessionsAsync(user.Id, ct);
        var mediaIds = sessions.Select(s => s.MediaId).Distinct().ToList();

        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.MediaIds = SqlKeys.ToJson(mediaIds);

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" IN (SELECT value FROM json_each(@MediaIds)) AND {CatalogSqlFilters.MediaVisible};
            """;

        var books = mediaIds.Count == 0
            ? []
            : (await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var booksMap = books.ToDictionary(b => b.Id);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var publications = new List<OpdsV2Publication>();

        foreach (var s in sessions)
        {
            if (booksMap.TryGetValue(s.MediaId, out var media))
            {
                publications.Add(ToPublication(media, apiKey, epubTotals.TotalFor(media.Id)));
            }
        }

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "Keep Reading",
                NumberOfItems = publications.Count
            },
            Links =
            [
                new(FormatUrl("books/keep-reading", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ],
            Publications = publications
        };
    }

    public async Task<OpdsV2Publication?> GetPublicationAsync(AuthUser user, string bookId, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = bookId;

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        var books = await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn);

        var book = books.FirstOrDefault();
        if (book == null) return null;

        var progress = await _progress.ResolveAsync(book, session: null, ct);
        return ToPublication(book, apiKey, progress.TotalPages, includeReadingOrder: true);
    }

    public async Task<OpdsV2Progression?> GetProgressionAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var session = await conn.QueryFirstOrDefaultAsync<ReadingSession>(
            """
                SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                       "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                       "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                       "RenderedPage", "RenderedTotalPages", "RenderedProfileKey"
                FROM "ReadingSessions"
                WHERE "MediaId" = @BookId AND "UserId" = @UserId
                ORDER BY "Id" DESC
                LIMIT 1;
                """, new { BookId = bookId, UserId = user.Id });

        if (session == null) return null;

        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = bookId;

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        var book = await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);
        if (book == null) return null;

        var progress = await _progress.ResolveAsync(book, session, ct);
        return new OpdsV2Progression
        {
            Page = progress.Page,
            Percentage = progress.Page is int p && progress.TotalPages > 0
                ? Math.Clamp((decimal)p / progress.TotalPages, 0m, 1m)
                : session.EndPercentage,
            Modified = session.UpdatedAt?.ToString("O") ?? session.CreatedAt.ToString("O"),
            Device = "OPDS-2.0-Client"
        };
    }

    public async Task<bool> UpdateProgressionAsync(AuthUser user, string bookId, OpdsV2Progression progression, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = bookId;

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        var book = await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);
        if (book == null) return false;

        var page = progression.Page ?? 1;
        var ok = await _progress.RecordAsync(
            user,
            book,
            new ReadingProgressUpdate(page, Percentage: progression.Percentage),
            ct);

        if (ok)
        {
            _logger.LogTrace("Updated OPDS 2.0 reading progression for user {UserId}, book {BookId}, page {Page}", user.Id, bookId, page);
        }
        return ok;
    }

    private OpdsV2Publication ToPublication(
        Media media,
        string? apiKey,
        int? totalPages = null,
        bool includeReadingOrder = false)
    {
        var title = media.Metadata?.Title ?? media.Name;
        var summary = media.Metadata?.Summary;
        var modified = (media.UpdatedAt ?? media.CreatedAt).ToString("O");
        var ext = ContentTypeExtensions.FromExtension(media.Extension);

        // The pages the reader will request, not those of the OPF: in an EPUB they are the
        // rendered ones.
        var pages = totalPages is > 0 ? totalPages.Value : media.Pages;

        var publication = new OpdsV2Publication
        {
            Metadata = new OpdsV2Metadata
            {
                Title = title,
                Identifier = $"urn:uuid:{media.Id}",
                Type = "http" + "://schema.org/ComicStory",
                Modified = modified,
                Description = summary,
                NumberOfPages = pages,
                ReadingProgression = "ltr"
            },
            Images =
            [
                new(FormatUrl($"books/{media.Id}/thumbnail", apiKey), "image/jpeg")
            ],
            Links =
            [
                new(FormatUrl($"books/{media.Id}", apiKey), OpdsV2MimeTypes.PublicationJson, "self"),
                new(FormatUrl($"books/{media.Id}/file", apiKey), ext.ToMimeType(), "http" + "://opds-spec.org/acquisition"),
                new(FormatUrl($"books/{media.Id}/progression", apiKey), "application/json", "http" + "://vaemendis.net/opds-pse/stream")
            ]
        };

        if (includeReadingOrder && pages > 0)
        {
            for (var p = 0; p < pages; p++)
            {
                publication.ReadingOrder.Add(new OpdsV2Link(
                    FormatUrl($"books/{media.Id}/pages/{p}?zero_based=true", apiKey),
                    "image/jpeg"
                ));
            }
        }

        return publication;
    }
}
