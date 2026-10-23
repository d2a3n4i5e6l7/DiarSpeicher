using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Komga;

public class KomgaService : IKomgaService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<KomgaService> _logger;

    private readonly ICompositeBookProcessor? _pageProcessor;
    private readonly IEpubProfileProvider? _epubProfileProvider;

    private readonly IReadingProgress _progress;

    public KomgaService(
        ISqliteConnectionFactory connectionFactory,
        ILogger<KomgaService> logger,
        IReadingProgress progress,
        ICompositeBookProcessor? pageProcessor = null,
        IEpubProfileProvider? epubProfileProvider = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _pageProcessor = pageProcessor;
        _epubProfileProvider = epubProfileProvider;
        _progress = progress;
    }

    public async Task<List<KomgaLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default)
    {
        _logger.LogTrace("Fetching Komga libraries for user {UserId}", user.Id);
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        var sql = $"""
            SELECT l."Id", l."CreatedAt", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {where}
            ORDER BY l."Name" ASC;
            """;

        var libraries = await conn.QueryAsync<Library>(sql, parameters);
        return libraries.Select(KomgaDtoMapper.ToLibraryDto).ToList();
    }

    public async Task<KomgaLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        parameters.Add("Id", id);

        var sql = $"""
            SELECT l."Id", l."CreatedAt", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE l."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var lib = await conn.QueryFirstOrDefaultAsync<Library>(sql, parameters);
        return lib == null ? null : KomgaDtoMapper.ToLibraryDto(lib);
    }

    public async Task<KomgaPageResponse<KomgaSeriesDto>> GetSeriesAsync(
        AuthUser user,
        string? libraryId,
        string? search,
        int page,
        int size,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");

        var extraWhere = new List<string> { where };
        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            extraWhere.Add("s.\"LibraryId\" = @LibraryId");
            parameters.Add("LibraryId", libraryId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            extraWhere.Add("(s.\"Name\" LIKE @SearchPattern OR sm.\"Title\" LIKE @SearchPattern)");
            parameters.Add("SearchPattern", $"%{search}%");
        }

        var fullWhere = string.Join(" AND ", extraWhere);

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere};
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        var pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "MediaCount"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere}
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", size);
        parameters.Add("Skip", page * size);

        var seriesRows = await conn.QueryAsync<CountedSeries>(pagedSql, parameters).WithMetadataAsync(conn);

        var seriesDtos = seriesRows.Select(row => KomgaDtoMapper.ToSeriesDto(row, (int)row.MediaCount)).ToList();
        return KomgaPageResponse<KomgaSeriesDto>.Create(seriesDtos, page, size, totalElements);
    }

    public async Task<KomgaSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        parameters.Add("Id", id);

        var sql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "MediaCount"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<CountedSeries>(sql, parameters).WithMetadataAsync(conn);

        var first = rows.FirstOrDefault();
        return first == null ? null : KomgaDtoMapper.ToSeriesDto(first, (int)first.MediaCount);
    }

    public async Task<KomgaPageResponse<KomgaBookDto>> GetBooksAsync(
        AuthUser user,
        string? search,
        int page,
        int size,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");

        if (!string.IsNullOrWhiteSpace(search))
        {
            where = $"{where} AND (m.\"Name\" LIKE @SearchPattern OR mm.\"Title\" LIKE @SearchPattern)";
            parameters.Add("SearchPattern", $"%{search}%");
        }

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where};
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        var pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", size);
        parameters.Add("Skip", page * size);

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
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where};
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        var pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."CreatedAt" DESC, m."Id" DESC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", size);
        parameters.Add("Skip", page * size);

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
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("SeriesId", seriesId);

        var fullWhere = $"{where} AND m.\"SeriesId\" = @SeriesId";

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere};
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        var pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", size);
        parameters.Add("Skip", page * size);

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var bookDtos = books.Select(b => KomgaDtoMapper.ToBookDto(b, sessions.GetValueOrDefault(b.Id), epubTotals: epubTotals)).ToList();

        return KomgaPageResponse<KomgaBookDto>.Create(bookDtos, page, size, totalElements);
    }

    public async Task<KomgaBookDto?> GetBookByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("Id", id);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {where}
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
                           "RenderedPage", "RenderedTotalPages", "RenderedProfileKey"
                    FROM "ReadingSessions"
                    WHERE "MediaId" = @Id AND "UserId" = @UserId
                    ORDER BY "Id" DESC
                    LIMIT 1;
                    """, new { Id = id, UserId = user.Id });
        }

        var progress = await _progress.ResolveAsync(book, session, ct);
        return KomgaDtoMapper.ToBookDto(book, session, progress.TotalPages, overrideCurrentPage: progress.Page);
    }

    public async Task<List<KomgaBookPageDto>> GetBookPagesAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("Id", id);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var book = await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);
        if (book == null || book.Pages <= 0) return [];

        if (EpubPageMapStore.IsEpub(book))
        {
            return await GetEpubBookPagesAsync(book, ct);
        }

        var pageSql = """
            SELECT "FileName", "Height", "MediaId", "MediaType", "Number", "SizeBytes", "Width"
            FROM "MediaPages"
            WHERE "MediaId" = @MediaId
            ORDER BY "Number" ASC;
            """;

        var measured = (await conn.QueryAsync<MediaPage>(
            pageSql, new { MediaId = book.Id })).ToList();

        if (measured.Count == 0)
        {
            measured = await MeasurePagesAsync(book, ct);
        }

        if (measured.Count > 0)
        {
            return measured.Select(p => new KomgaBookPageDto
            {
                Number = p.Number,
                FileName = p.FileName,
                MediaType = p.MediaType,
                Width = p.Width,
                Height = p.Height,
                SizeBytes = p.SizeBytes
            }).ToList();
        }

        var fallbackPages = new List<KomgaBookPageDto>();
        for (var i = 1; i <= book.Pages; i++)
        {
            fallbackPages.Add(new KomgaBookPageDto
            {
                Number = i,
                FileName = $"page_{i:D4}.jpg",
                MediaType = "image/jpeg"
            });
        }
        return fallbackPages;
    }

    private async Task<List<MediaPage>> MeasurePagesAsync(Media book, CancellationToken ct)
    {
        if (_pageProcessor is null) return [];
        if (EpubPageMapStore.IsEpub(book)) return [];

        _logger.LogInformation(
            "Midiendo {Pages} paginas de {BookId}; la primera apertura de un libro es lenta",
            book.Pages, book.Id);

        var analyzed = await _pageProcessor.AnalyzeAsync(book.Path, new BookAnalysisOptions { MeasurePages = true }, ct);

        var rows = analyzed.PageDimensions.Select(p => new MediaPage
        {
            MediaId = book.Id,
            Number = p.Number,
            FileName = p.FileName,
            MediaType = p.MediaType,
            Width = p.Width,
            Height = p.Height,
            SizeBytes = p.SizeBytes
        }).ToList();

        if (rows.Count > 0)
        {
            await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
            const string insertSql = """
                INSERT INTO "MediaPages" ("MediaId", "Number", "FileName", "MediaType", "Width", "Height", "SizeBytes")
                VALUES (@MediaId, @Number, @FileName, @MediaType, @Width, @Height, @SizeBytes)
                ON CONFLICT ("MediaId", "Number") DO NOTHING;
                """;

            await conn.ExecuteAsync(insertSql, rows);

            const string selectSql = """
                SELECT "FileName", "Height", "MediaId", "MediaType", "Number", "SizeBytes", "Width"
                FROM "MediaPages"
                WHERE "MediaId" = @MediaId
                ORDER BY "Number" ASC;
                """;

            return (await conn.QueryAsync<MediaPage>(
                selectSql, new { MediaId = book.Id })).ToList();
        }

        return rows;
    }

    public async Task<bool> UpdateReadProgressAsync(
        AuthUser user,
        string bookId,
        int page,
        bool completed,
        CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("Id", bookId);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var book = await conn.QueryFirstOrDefaultAsync<Media>(sql, parameters);
        if (book == null) return false;

        var ok = await _progress.RecordAsync(user, book, new ReadingProgressUpdate(page, completed), ct);
        if (ok)
        {
            _logger.LogTrace("Updated Komga read progress for user {UserId}, book {BookId}, page {Page}", user.Id, bookId, page);
        }
        return ok;
    }

    public async Task<bool> DeleteReadProgressAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return false;

        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string deleteSql = """
            DELETE FROM "ReadingSessions"
            WHERE "MediaId" = @BookId AND "UserId" = @UserId;
            """;

        var rows = await conn.ExecuteAsync(deleteSql, new { BookId = bookId, UserId = user.Id });
        if (rows > 0)
        {
            _logger.LogTrace("Deleted Komga read progress for user {UserId}, book {BookId}", user.Id, bookId);
            return true;
        }
        return false;
    }

    private async Task CleanStalePagesAsync(string mediaId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = """DELETE FROM "MediaPages" WHERE "MediaId" = @MediaId;""";
        await conn.ExecuteAsync(sql, new { MediaId = mediaId });
    }

    private async Task<List<KomgaBookPageDto>> GetEpubBookPagesAsync(Media book, CancellationToken ct)
    {
        await CleanStalePagesAsync(book.Id, ct);

        var profile = _epubProfileProvider != null
            ? await _epubProfileProvider.GetCurrentProfileAsync(ct)
            : EpubDeviceProfile.GetDefaults()[0];

        await using var archive = await ZipFile.OpenReadAsync(book.Path, ct);
        var spine = await EpubBookProcessor.GetSpineEntriesAsync(archive, ct);
        if (spine.Count == 0) spine = EpubBookProcessor.GetFallbackHtmlEntries(archive);
        var cover = await EpubBookProcessor.FindCoverEntryAsync(archive, ct);
        var map = await EpubRasterizer.GetOrBuildPageMapAsync(archive, book.Path, spine, cover, profile, ct);

        var pages = new List<KomgaBookPageDto>(map.TotalPages);
        for (var i = 1; i <= map.TotalPages; i++)
        {
            pages.Add(BuildBookPageDto(map.Pages[i - 1], i, profile));
        }
        return pages;
    }

    private static KomgaBookPageDto BuildBookPageDto(EpubSubpageTarget target, int pageNumber, EpubDeviceProfile profile)
    {
        int width = profile.Width;
        int? height = profile.Height;
        string mediaType = "image/webp";

        if (target.IsImageOnly && target.ImageWidth.HasValue && target.ImageHeight.HasValue)
        {
            width = target.ImageWidth.Value;
            height = target.ImageHeight.Value;
            var ext = Path.GetExtension(target.EntryFullName).ToLowerInvariant();
            mediaType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        return new KomgaBookPageDto
        {
            Number = pageNumber,
            FileName = $"page_{pageNumber:D4}.webp",
            MediaType = mediaType,
            Width = width,
            Height = height
        };
    }
}
