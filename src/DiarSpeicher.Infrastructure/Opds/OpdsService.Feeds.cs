using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;

namespace DiarSpeicher.Infrastructure.Opds;

public partial class OpdsService
{
    public async Task<string> GetSeriesFeedAsync(AuthUser user, string? search, int page, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.SearchPattern = $"%{search}%";
        }

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.SeriesSearch};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status",
                   s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.SeriesSearch}
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = PageSize;
        parameters.Skip = page * PageSize;

        var seriesList = (await conn.QueryAsync<Series>(pagedSql, parameters).WithMetadataAsync(conn)).ToList();

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
        var parameters = CatalogSqlFilters.ArgsFor(user);

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible};
            """;

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        const string pagedSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status",
                   s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible}
            ORDER BY s."Id" DESC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = PageSize;
        parameters.Skip = page * PageSize;

        var seriesList = (await conn.QueryAsync<Series>(pagedSql, parameters).WithMetadataAsync(conn)).ToList();

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
            throw new KeyNotFoundException($"Series {seriesId} not found");
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

        var books = (await conn.QueryAsync<Media>(pagedSql, mParams).WithMetadataAsync(conn)).ToList();

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

        var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

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

        parameters.Take = PageSize;
        parameters.Skip = page * PageSize;

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn)).ToList();

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

        var books = (await conn.QueryAsync<Media>(pagedSql, parameters).WithMetadataAsync(conn)).ToList();

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
            : (await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn)).ToList();

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
        var lParams = CatalogSqlFilters.ArgsFor(user);
        lParams.SearchPattern = $"%{search}%";

        const string libSql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {CatalogSqlFilters.LibraryVisible} AND {CatalogSqlFilters.LibrarySearch}
            ORDER BY l."Name" ASC;
            """;

        var libraries = await conn.QueryAsync<Library>(libSql, lParams);
        entries.AddRange(libraries.Select(l => ToOpdsEntry(l, apiKey)));

        // 2. Series
        var sParams = CatalogSqlFilters.ArgsFor(user);
        sParams.SearchPattern = $"%{search}%";

        const string seriesSql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status",
                   s."UpdatedAt"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.SeriesVisible} AND {CatalogSqlFilters.SeriesSearch}
            ORDER BY s."Name" ASC;
            """;

        var seriesList = await conn.QueryAsync<Series>(seriesSql, sParams).WithMetadataAsync(conn);
        entries.AddRange(seriesList.Select(s => ToOpdsEntry(s, apiKey)));

        // 3. Books
        var mParams = CatalogSqlFilters.ArgsFor(user);
        mParams.SearchPattern = $"%{search}%";

        const string booksSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND {CatalogSqlFilters.MediaSearch}
            ORDER BY m."SortName" ASC, m."Name" ASC;
            """;

        var books = (await conn.QueryAsync<Media>(booksSql, mParams).WithMetadataAsync(conn)).ToList();

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Id).ToList(), ct);
        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        entries.AddRange(books.Select(b => ToOpdsEntry(b, sessions.GetValueOrDefault(b.Id), apiKey, epubTotals)));

        var feed = new OpdsFeed("searchFeed", "Search Results", links, entries);
        return OpdsXmlBuilder.BuildFeedXml(feed);
    }
}
