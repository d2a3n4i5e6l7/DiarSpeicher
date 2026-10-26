using Dapper;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Komga;

public partial class KomgaService : IKomgaService
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
        var parameters = CatalogSqlFilters.ArgsFor(user);
        const string sql = $"""
            SELECT l."Id", l."CreatedAt", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {CatalogSqlFilters.LibraryVisible}
            ORDER BY l."Name" ASC;
            """;

        var libraries = await conn.QueryAsync<Library>(sql, parameters);
        return libraries.Select(KomgaDtoMapper.ToLibraryDto).ToList();
    }

    public async Task<KomgaLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = id;

        const string sql = $"""
            SELECT l."Id", l."CreatedAt", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE l."Id" = @Id AND {CatalogSqlFilters.LibraryVisible}
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
        var parameters = CatalogSqlFilters.ArgsFor(user);

        if (!string.IsNullOrWhiteSpace(libraryId))
        {
            parameters.LibraryId = libraryId;
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.SearchPattern = $"%{search}%";
        }

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.InLibrary} AND {CatalogSqlFilters.SeriesSearch};
            """;

        var totalElements = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "MediaCount"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.InLibrary} AND {CatalogSqlFilters.SeriesSearch}
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = size;
        parameters.Skip = page * size;

        var seriesRows = await conn.QueryAsync<CountedSeries>(pagedSql, parameters).WithMetadataAsync(conn);

        var seriesDtos = seriesRows.Select(row => KomgaDtoMapper.ToSeriesDto(row, (int)row.MediaCount)).ToList();
        return KomgaPageResponse<KomgaSeriesDto>.Create(seriesDtos, page, size, totalElements);
    }

    public async Task<KomgaSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = id;

        const string sql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "MediaCount"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @Id AND {CatalogSqlFilters.SeriesVisible}
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<CountedSeries>(sql, parameters).WithMetadataAsync(conn);

        var first = rows.FirstOrDefault();
        return first == null ? null : KomgaDtoMapper.ToSeriesDto(first, (int)first.MediaCount);
    }
}
