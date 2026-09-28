using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Core.Gateway;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public class OpdsService : IOpdsService
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
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");

        if (!string.IsNullOrWhiteSpace(search))
        {
            where = $"{where} AND l.\"Name\" LIKE @SearchPattern";
            parameters.Add("SearchPattern", $"%{search}%");
        }

        var sql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {where}
            ORDER BY l."Name" ASC;
            """;

        var libraries = await conn.QueryAsync<Library>(new CommandDefinition(sql, parameters, cancellationToken: ct));
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
        var (libWhere, libParams) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        libParams.Add("LibraryId", libraryId);

        var libSql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE l."Id" = @LibraryId AND {libWhere}
            LIMIT 1;
            """;

        var library = await conn.QueryFirstOrDefaultAsync<Library>(new CommandDefinition(libSql, libParams, cancellationToken: ct));
        if (library == null)
        {
            throw new KeyNotFoundException($"Library {libraryId} not found");
        }

        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        var fullWhere = $"{where} AND s.\"LibraryId\" = @LibraryId";
        parameters.Add("LibraryId", libraryId);

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

        var pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   sm."SeriesId", sm."AgeRating", sm."Booktype", sm."Comicid", sm."ComicImage", sm."Genres", sm."Links", sm."MetaType", sm."PublicationRun", sm."Publisher", sm."Status", sm."Summary", sm."Title", sm."TotalIssues", sm."Volume", sm."Writers", sm."Year"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere}
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", PageSize);
        parameters.Add("Skip", page * PageSize);

        var seriesList = (await conn.QueryAsync<Series, SeriesMetadata, Series>(
            pagedSql,
            (s, sm) =>
            {
                s.Metadata = sm;
                return s;
            },
            parameters,
            splitOn: "SeriesId")).ToList();

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

    public async Task<string> GetSeriesFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");

        if (!string.IsNullOrWhiteSpace(search))
        {
            where = $"{where} AND (s.\"Name\" LIKE @SearchPattern OR sm.\"Title\" LIKE @SearchPattern)";
            parameters.Add("SearchPattern", $"%{search}%");
        }

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

        var pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   sm."SeriesId", sm."AgeRating", sm."Booktype", sm."Comicid", sm."ComicImage", sm."Genres", sm."Links", sm."MetaType", sm."PublicationRun", sm."Publisher", sm."Status", sm."Summary", sm."Title", sm."TotalIssues", sm."Volume", sm."Writers", sm."Year"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", PageSize);
        parameters.Add("Skip", page * PageSize);

        var seriesList = (await conn.QueryAsync<Series, SeriesMetadata, Series>(
            pagedSql,
            (s, sm) =>
            {
                s.Metadata = sm;
                return s;
            },
            parameters,
            splitOn: "SeriesId")).ToList();

        var entries = seriesList.Select(s => ToOpdsEntry(s, apiKey)).ToList();
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "allSeries",
            "All Series",
            entries,
            "series",
            page,
            totalCount,
            search,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetLatestSeriesFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

        var pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   sm."SeriesId", sm."AgeRating", sm."Booktype", sm."Comicid", sm."ComicImage", sm."Genres", sm."Links", sm."MetaType", sm."PublicationRun", sm."Publisher", sm."Status", sm."Summary", sm."Title", sm."TotalIssues", sm."Volume", sm."Writers", sm."Year"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY s."Id" DESC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", PageSize);
        parameters.Add("Skip", page * PageSize);

        var seriesList = (await conn.QueryAsync<Series, SeriesMetadata, Series>(
            pagedSql,
            (s, sm) =>
            {
                s.Metadata = sm;
                return s;
            },
            parameters,
            splitOn: "SeriesId")).ToList();

        var entries = seriesList.Select(s => ToOpdsEntry(s, apiKey)).ToList();
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "latestSeries",
            "Latest Series",
            entries,
            "series/latest",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetSeriesBooksFeedAsync(AuthUser user, string seriesId, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (sWhere, sParams) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        sParams.Add("SeriesId", seriesId);

        var seriesSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   sm."SeriesId", sm."AgeRating", sm."Booktype", sm."Comicid", sm."ComicImage", sm."Genres", sm."Links", sm."MetaType", sm."PublicationRun", sm."Publisher", sm."Status", sm."Summary", sm."Title", sm."TotalIssues", sm."Volume", sm."Writers", sm."Year"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @SeriesId AND {sWhere}
            LIMIT 1;
            """;

        var seriesRows = await conn.QueryAsync<Series, SeriesMetadata, Series>(
            seriesSql,
            (s, sm) =>
            {
                s.Metadata = sm;
                return s;
            },
            sParams,
            splitOn: "SeriesId");

        var series = seriesRows.FirstOrDefault();
        if (series == null)
        {
            throw new KeyNotFoundException($"Series {seriesId} not found");
        }

        var (mWhere, mParams) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        var fullWhere = $"{mWhere} AND m.\"SeriesId\" = @SeriesId";
        mParams.Add("SeriesId", seriesId);

        var countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, mParams, cancellationToken: ct));

        var pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {fullWhere}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        mParams.Add("Take", PageSize);
        mParams.Add("Skip", page * PageSize);

        var books = (await conn.QueryAsync<Media, MediaMetadata, Media>(
            pagedSql,
            (media, meta) =>
            {
                media.Metadata = meta;
                return media;
            },
            mParams,
            splitOn: "MediaId")).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var entries = books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey, epubTotals)).ToList();

        var title = series.Metadata?.Title ?? series.Name;
        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            series.Id,
            title,
            entries,
            $"series/{series.Id}",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetBooksFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default)
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

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

        var pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", PageSize);
        parameters.Add("Skip", page * PageSize);

        var books = (await conn.QueryAsync<Media, MediaMetadata, Media>(
            pagedSql,
            (media, meta) =>
            {
                media.Metadata = meta;
                return media;
            },
            parameters,
            splitOn: "MediaId")).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var entries = books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey, epubTotals)).ToList();

        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "allBooks",
            "All Books",
            entries,
            "books",
            page,
            totalCount,
            search,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetLatestBooksFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
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

        var totalCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, parameters, cancellationToken: ct));

        var pagedSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."Id" DESC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Add("Take", PageSize);
        parameters.Add("Skip", page * PageSize);

        var books = (await conn.QueryAsync<Media, MediaMetadata, Media>(
            pagedSql,
            (media, meta) =>
            {
                media.Metadata = meta;
                return media;
            },
            parameters,
            splitOn: "MediaId")).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var entries = books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey, epubTotals)).ToList();

        var feed = CreatePaginatedFeed(new PaginatedFeedParams(
            "latestBooks",
            "Latest Books",
            entries,
            "books/latest",
            page,
            totalCount,
            null,
            apiKey));

        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetKeepReadingFeedXmlAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var sessions = await conn.GetKeepReadingSessionsAsync(user.Id, ct);
        var mediaIds = sessions.Select(s => s.MediaId).Distinct().ToList();

        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("MediaIds", mediaIds);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" IN @MediaIds AND {where};
            """;

        var books = mediaIds.Count == 0
            ? []
            : (await conn.QueryAsync<Media, MediaMetadata, Media>(
                sql,
                (media, meta) =>
                {
                    media.Metadata = meta;
                    return media;
                },
                parameters,
                splitOn: "MediaId")).ToList();

        var booksMap = books.ToDictionary(b => b.Id);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        var entries = new List<OpdsEntry>();

        foreach (var s in sessions)
        {
            if (booksMap.TryGetValue(s.MediaId, out var media))
            {
                entries.Add(ToOpdsEntry(media, s, apiKey, epubTotals));
            }
        }

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatUrl("keep-reading", apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey))
        };

        var feed = new OpdsFeed("keepReading", "Keep Reading", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<string> GetSearchFeedXmlAsync(AuthUser user, string? query, string? apiKey, CancellationToken ct = default)
    {
        var search = query?.Trim() ?? string.Empty;
        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatParams("search/feed", new Dictionary<string, string> { { SearchLiteral, search } }, apiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, apiKey))
        };

        if (string.IsNullOrWhiteSpace(search))
        {
            var emptyFeed = new OpdsFeed("searchFeed", "Search Results", links, []);
            return OpdsXmlBuilder.BuildFeedXml(emptyFeed);
        }

        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var entries = new List<OpdsEntry>();

        // 1. Libraries
        var (lWhere, lParams) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        lWhere = $"{lWhere} AND l.\"Name\" LIKE @SearchPattern";
        lParams.Add("SearchPattern", $"%{search}%");

        var libSql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {lWhere}
            ORDER BY l."Name" ASC;
            """;

        var libraries = await conn.QueryAsync<Library>(new CommandDefinition(libSql, lParams, cancellationToken: ct));
        entries.AddRange(libraries.Select(l => ToOpdsEntry(l, apiKey)));

        // 2. Series
        var (sWhere, sParams) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        sWhere = $"{sWhere} AND (s.\"Name\" LIKE @SearchPattern OR sm.\"Title\" LIKE @SearchPattern)";
        sParams.Add("SearchPattern", $"%{search}%");

        var seriesSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   sm."SeriesId", sm."AgeRating", sm."Booktype", sm."Comicid", sm."ComicImage", sm."Genres", sm."Links", sm."MetaType", sm."PublicationRun", sm."Publisher", sm."Status", sm."Summary", sm."Title", sm."TotalIssues", sm."Volume", sm."Writers", sm."Year"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {sWhere}
            ORDER BY s."Name" ASC;
            """;

        var seriesList = await conn.QueryAsync<Series, SeriesMetadata, Series>(
            seriesSql,
            (s, sm) =>
            {
                s.Metadata = sm;
                return s;
            },
            sParams,
            splitOn: "SeriesId");
        entries.AddRange(seriesList.Select(s => ToOpdsEntry(s, apiKey)));

        // 3. Books
        var (mWhere, mParams) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        mWhere = $"{mWhere} AND (m.\"Name\" LIKE @SearchPattern OR mm.\"Title\" LIKE @SearchPattern)";
        mParams.Add("SearchPattern", $"%{search}%");

        var booksSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {mWhere}
            ORDER BY m."SortName" ASC, m."Name" ASC;
            """;

        var books = (await conn.QueryAsync<Media, MediaMetadata, Media>(
            booksSql,
            (media, meta) =>
            {
                media.Metadata = meta;
                return media;
            },
            mParams,
            splitOn: "MediaId")).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        entries.AddRange(books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey, epubTotals)));

        var feed = new OpdsFeed("searchFeed", "Search Results", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }

    public async Task<(OpenedPage? Page, Media? Media)> OpenBookPageAsync(
        AuthUser user,
        string bookId,
        int pageNumber,
        PageRequestKind kind = PageRequestKind.Reading,
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

        var book = await conn.QueryFirstOrDefaultAsync<Media>(new CommandDefinition(sql, parameters, cancellationToken: ct));

        if (book == null) return (null, null);

        var correctPage = pageNumber;
        var isEpub = EpubPageMapStore.IsEpub(book);
        if (correctPage < 1 || (!isEpub && book.Pages > 0 && correctPage > book.Pages))
        {
            return (null, book);
        }

        if (kind == PageRequestKind.Reading && !string.IsNullOrWhiteSpace(user.Id))
        {
            await RecordReadingProgressAsync(user, book, correctPage, ct);
        }

        var page = await _bookProcessor.OpenPageAsync(book.Path, correctPage, ct);
        return (page, book);
    }

    private async Task RecordReadingProgressAsync(AuthUser user, Media book, int correctPage, CancellationToken ct)
    {
        try
        {
            await _progress.RecordAsync(user, book, new ReadingProgressUpdate(correctPage), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record reading progression for user {UserId}, book {BookId}", user.Id, book.Id);
        }
    }

    public async Task<Media?> GetMediaForDownloadAsync(AuthUser user, string bookId, CancellationToken ct = default)
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

        return await conn.QueryFirstOrDefaultAsync<Media>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public async Task<(byte[]? Data, string ContentType)> GetBookThumbnailAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        var book = await GetMediaForDownloadAsync(user, bookId, ct);
        if (book == null) return (null, "image/jpeg");

        if (!string.IsNullOrWhiteSpace(book.ThumbnailPath) && File.Exists(book.ThumbnailPath))
        {
            var bytes = await File.ReadAllBytesAsync(book.ThumbnailPath, ct);
            var ext = Path.GetExtension(book.ThumbnailPath);
            var mime = ContentTypeExtensions.FromExtension(ext).MimeType();
            return (bytes, mime);
        }

        return (null, "image/jpeg");
    }

    private OpdsEntry ToOpdsEntry(Library library, string? apiKey)
    {
        return new OpdsEntry
        {
            Id = library.Id,
            Title = library.Name,
            Updated = library.UpdatedAt ?? library.CreatedAt,
            Content = library.Description,
            Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl($"libraries/{library.Id}", apiKey))]
        };
    }

    private OpdsEntry ToOpdsEntry(Series series, string? apiKey)
    {
        var title = series.Metadata?.Title ?? series.Name;
        var content = series.Metadata?.Summary ?? series.Description;

        return new OpdsEntry
        {
            Id = series.Id,
            Title = title,
            Updated = series.UpdatedAt ?? series.CreatedAt,
            Content = content,
            Links = [new(OpdsLinkType.Navigation, OpdsLinkRel.Subsection, FormatUrl($"series/{series.Id}", apiKey))]
        };
    }

    private OpdsEntry ToOpdsEntry(
        Media media,
        ReadingSession? session,
        string? apiKey,
        IReadOnlyDictionary<string, int>? epubTotals = null)
    {
        var title = media.Metadata?.Title ?? media.Name;
        var summary = media.Metadata?.Summary;
        var mib = media.Size / (1024.0 * 1024.0);
        var content = !string.IsNullOrWhiteSpace(summary)
            ? $"{mib:F1} MiB - {media.Extension}<br/><br/>{summary}"
            : $"{mib:F1} MiB - {media.Extension}";

        List<string>? authors = null;
        if (!string.IsNullOrWhiteSpace(media.Metadata?.Writers))
        {
            authors = media.Metadata.Writers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var fileName = Uri.EscapeDataString(Path.GetFileName(media.Path));
        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Thumbnail, FormatUrl($"books/{media.Id}/thumbnail", apiKey)),
            new(OpdsLinkType.ImageJpeg, OpdsLinkRel.Image, FormatUrl($"books/{media.Id}/pages/0?zero_based=true&cover=1", apiKey)),
            new(OpdsLinkType.FromExtension(media.Extension), OpdsLinkRel.Acquisition, FormatUrl($"books/{media.Id}/file/{fileName}", apiKey))
        };

        var progress = IReadingProgress.FromSession(media, session, epubTotals.TotalFor(media.Id));

        var streamLink = new OpdsStreamLink(
            media.Id,
            progress.TotalPages,
            OpdsLinkType.ImageJpeg,
            progress.Page,
            session?.UpdatedAt?.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
            FormatUrl($"books/{media.Id}/pages/{{pageNumber}}?zero_based=true", apiKey)
        );

        return new OpdsEntry
        {
            Id = media.Id,
            Title = title,
            Updated = media.UpdatedAt ?? media.CreatedAt,
            Summary = summary,
            Content = content,
            Authors = authors,
            Links = links,
            StreamLink = streamLink
        };
    }

    private OpdsFeed CreatePaginatedFeed(PaginatedFeedParams p)
    {
        var thisParams = new Dictionary<string, string> { { "page", p.Page.ToString() } };
        if (!string.IsNullOrWhiteSpace(p.Search)) thisParams[SearchLiteral] = p.Search;

        var links = new List<OpdsLink>
        {
            new(OpdsLinkType.Navigation, OpdsLinkRel.ItSelf, FormatParams(p.HrefPostfix, thisParams, p.ApiKey)),
            new(OpdsLinkType.Navigation, OpdsLinkRel.Start, FormatUrl(CatalogEndpoint, p.ApiKey))
        };

        if (p.Page > 0)
        {
            var prevParams = new Dictionary<string, string> { { "page", (p.Page - 1).ToString() } };
            if (!string.IsNullOrWhiteSpace(p.Search)) prevParams[SearchLiteral] = p.Search;
            links.Add(new(OpdsLinkType.Navigation, OpdsLinkRel.Previous, FormatParams(p.HrefPostfix, prevParams, p.ApiKey)));
        }

        var totalPages = (int)Math.Ceiling(p.TotalCount / (double)PageSize);
        if (p.Page < totalPages - 1 && p.Entries.Count == PageSize)
        {
            var nextParams = new Dictionary<string, string> { { "page", (p.Page + 1).ToString() } };
            if (!string.IsNullOrWhiteSpace(p.Search)) nextParams[SearchLiteral] = p.Search;
            links.Add(new(OpdsLinkType.Navigation, OpdsLinkRel.Next, FormatParams(p.HrefPostfix, nextParams, p.ApiKey)));
        }

        return new OpdsFeed(p.Id, p.Title, links, p.Entries);
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
