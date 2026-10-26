using Dapper;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;

namespace DiarSpeicher.Infrastructure.Komga;

public partial class KomgaService
{
    public async Task<KomgaPageResponse<KomgaBookDto>> GetBooksAsync(
        AuthUser user,
        string? search,
        int page,
        int size,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.SearchPattern = $"%{search}%";
        }

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND {CatalogSqlFilters.MediaSearch};
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND {CatalogSqlFilters.MediaSearch}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = size;
        parameters.Skip = page * size;

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetLatestBooksAsync(
        AuthUser user,
        int page,
        int size,
        CancellationToken ct = default)
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

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible}
            ORDER BY m."CreatedAt" DESC, m."Id" DESC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = size;
        parameters.Skip = page * size;

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetBooksInSeriesAsync(
        AuthUser user,
        string seriesId,
        int page,
        int size,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.SeriesId = seriesId;

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND m."SeriesId" = @SeriesId;
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

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

        parameters.Take = size;
        parameters.Skip = page * size;

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaBookDto?> GetBookByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = id;

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

        ReadingSession? session = null;
        if (!string.IsNullOrWhiteSpace(user.Id))
        {
            session = await conn.QueryFirstOrDefaultAsync<ReadingSession>(
                """
                    SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                           "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                           "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                           "RenderedPage", "RenderedTotalPages", "RenderedProfileKey", "RenderedStatus"
                    FROM "ReadingSessions"
                    WHERE "MediaId" = @Id AND "UserId" = @UserId
                    ORDER BY "Id" DESC
                    LIMIT 1;
                    """, new { Id = id, UserId = user.Id });
        }

        var progress = await _progress.ResolveAsync(book, session, ct);
        return KomgaDtoMapper.ToBookDto(book, session, progress.TotalPages, overrideCurrentPage: progress.Page);
    }
}
