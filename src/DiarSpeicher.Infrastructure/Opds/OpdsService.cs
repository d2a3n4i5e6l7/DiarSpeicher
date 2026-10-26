using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Gateway;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public partial class OpdsService : IOpdsService
{
    private const int PageSize = 20;
    private const string CatalogEndpoint = "catalog";
    private const string SearchLiteral = "search";
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly ILogger<OpdsService> _logger;
    private readonly ILinkPrefixProvider _linkPrefix;
    private readonly IReadingProgress _progress;

    public OpdsService(
        ISqliteConnectionFactory connectionFactory,
        ICompositeBookProcessor bookProcessor,
        ILogger<OpdsService> logger,
        ILinkPrefixProvider linkPrefix,
        IReadingProgress progress)
    {
        _connectionFactory = connectionFactory;
        _bookProcessor = bookProcessor;
        _logger = logger;
        _linkPrefix = linkPrefix;
        _progress = progress;
    }

    private string FormatUrl(string path, string? apiKey)
    {
        var prefix = _linkPrefix.Prefix;

        return !string.IsNullOrWhiteSpace(apiKey)
            ? $"{prefix}/opds/{apiKey}/v1.2/{path}"
            : $"{prefix}/opds/v1.2/{path}";
    }

    private string FormatParams(string path, Dictionary<string, string> queryParams, string? apiKey)
    {
        var url = FormatUrl(path, apiKey);
        if (queryParams.Count == 0) return url;

        var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{url}?{queryString}";
    }

    public Task<string> GetCatalogXmlAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        var entries = new List<OpdsEntry>
        {
            new()
            {
                Id = "keepReading",
                Title = "Keep reading",
                Summary = "Continue reading your in progress books",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("keep-reading", apiKey))]
            },
            new()
            {
                Id = "allSeries",
                Title = "All series",
                Summary = "Browse by series",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("series", apiKey))]
            },
            new()
            {
                Id = "latestSeries",
                Title = "Latest series",
                Summary = "Browse latest series",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("series/latest", apiKey))]
            },
            new()
            {
                Id = "allLibraries",
                Title = "All libraries",
                Summary = "Browse by library",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("libraries", apiKey))]
            },
            new()
            {
                Id = "allBooks",
                Title = "All books",
                Summary = "Browse all books",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("books", apiKey))]
            },
            new()
            {
                Id = "latestBooks",
                Title = "Latest books",
                Summary = "Browse latest books",
                Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl("books/latest", apiKey))]
            }
        };

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatUrl(CatalogEndpoint, apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey)),
            new(OpdsLinkType.Search, OpdsLinkRel.Search, FormatUrl(SearchLiteral, apiKey))
        };

        var feed = new OpdsFeed("root", "DiarSpeicher OPDS Catalog", links, entries);
        return Task.FromResult(OpdsXmlBuilder.BuildFeedXml(feed));
    }

    public string GetOpenSearchXml(string? apiKey)
    {
        var searchUrl = FormatUrl("search/feed?search={searchTerms}", apiKey);
        return OpdsXmlBuilder.BuildOpenSearchXml(searchUrl);
    }

    public async Task<string> GetLibrariesFeedAsync(AuthUser user, string? search, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.SearchPattern = $"%{search}%";
        }

        const string sql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {CatalogSqlFilters.LibraryVisible} AND {CatalogSqlFilters.LibrarySearch}
            ORDER BY l."Name" ASC;
            """;

        var libraries = await conn.QueryAsync<Library>(sql, parameters);
        var entries = libraries.Select(l => ToOpdsEntry(l, apiKey)).ToList();

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatUrl("libraries", apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey))
        };

        var feed = new OpdsFeed("allLibraries", "All libraries", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetLibrarySeriesFeedAsync(AuthUser user, string libraryId, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var libParams = CatalogSqlFilters.ArgsFor(user);
        libParams.LibraryId = libraryId;

        const string libSql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE l."Id" = @LibraryId AND {CatalogSqlFilters.LibraryVisible}
            LIMIT 1;
            """;

        var library = await conn.QueryFirstOrDefaultAsync<Library>(libSql, libParams);
        if (library == null)
        {
            throw new KeyNotFoundException($"Library {libraryId} not found");
        }

        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.LibraryId = libraryId;

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND s."LibraryId" = @LibraryId;
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status",
                   s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND s."LibraryId" = @LibraryId
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = PageSize;
        parameters.Skip = page * PageSize;

        var seriesList = (await conn.QueryAsync<Series>(pagedSql, parameters).WithMetadataAsync(conn)).ToList();

        var entries = seriesList.Select(s => ToOpdsEntry(s, apiKey)).ToList();
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            library.Id,
            library.Name,
            entries,
            $"libraries/{library.Id}",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    private sealed record PaginatedFeedParams(
        string Id,
        string Title,
        List<OpdsEntry> Entries,
        string HrefPostfix,
        int Page,
        int TotalCount,
        string? Search,
        string? ApiKey);
}
