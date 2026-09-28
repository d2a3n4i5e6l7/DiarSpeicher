using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Core.Gateway;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public class OpdsV2Service : IOpdsV2Service
{
    private const int PageSize = 20;
    private const string CatalogEndpoint = "catalog";
    private const string StartRel = "start";
    private const string BasicAuthFlow = "http" + "://opds-spec.org/auth/basic";
    private const string LocalAuthFlow = "http" + "://opds-spec.org/auth/local";
    private const string TitleKey = "title";

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<OpdsV2Service> _logger;
    private readonly ILinkPrefixProvider _linkPrefix;
    private readonly IReadingProgress _progress;

    public OpdsV2Service(
        ISqliteConnectionFactory connectionFactory,
        ILogger<OpdsV2Service> logger,
        ILinkPrefixProvider linkPrefix,
        IReadingProgress progress)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _linkPrefix = linkPrefix;
        _progress = progress;
    }

    private string FormatUrl(string path, string? apiKey)
    {
        var prefix = _linkPrefix.Prefix;

        return !string.IsNullOrWhiteSpace(apiKey)
            ? $"{prefix}/opds/{apiKey}/v2.0/{path}"
            : $"{prefix}/opds/v2.0/{path}";
    }

    public OpdsV2AuthenticationDoc GetAuthenticationDoc(string? apiKey)
    {
        return new OpdsV2AuthenticationDoc
        {
            Id = FormatUrl("auth", apiKey),
            Title = "DiarSpeicher OPDS 2.0",
            Authentication =
            [
                new(BasicAuthFlow)
                {
                    Labels = new Dictionary<string, string>
                    {
                        ["login"] = "Username",
                        ["password"] = "Password"
                    }
                },
                new(LocalAuthFlow)
                {
                    Labels = new Dictionary<string, string>
                    {
                        ["login"] = "Username",
                        ["password"] = "Password"
                    }
                }
            ],
            Links =
            [
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ]
        };
    }

    public async Task<OpdsV2Feed> GetCatalogFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        _logger.LogTrace("Generating OPDS 2.0 root catalog for user {UserId}", user.Id);

        var feed = new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "DiarSpeicher OPDS 2.0 Catalog",
                Modified = DateTimeOffset.UtcNow.ToString("O")
            },
            Links =
            [
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel),
                new(FormatUrl("search{?query}", apiKey), OpdsV2MimeTypes.OpdsJson, "search") { Templated = true }
            ],
            Navigation =
            [
                new(FormatUrl("libraries", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "Libraries" } },
                new(FormatUrl("series", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "All Series" } },
                new(FormatUrl("books/browse", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "All Books" } },
                new(FormatUrl("books/latest", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "Latest Books" } },
                new(FormatUrl("books/keep-reading", apiKey), OpdsV2MimeTypes.OpdsJson) { Properties = new() { [TitleKey] = "Keep Reading" } }
            ]
        };

        var keepReadingFeed = await GetKeepReadingFeedAsync(user, apiKey, ct);
        if (keepReadingFeed.Publications.Count > 0)
        {
            feed.Groups.Add(new OpdsV2FeedGroup
            {
                Metadata = new OpdsV2Metadata { Title = "Keep Reading" },
                Links = [new(FormatUrl("books/keep-reading", apiKey), OpdsV2MimeTypes.OpdsJson, "self")],
                Publications = keepReadingFeed.Publications
            });
        }

        var latestBooksFeed = await GetBooksFeedAsync(user, 0, apiKey, ct);
        if (latestBooksFeed.Publications.Count > 0)
        {
            feed.Groups.Add(new OpdsV2FeedGroup
            {
                Metadata = new OpdsV2Metadata { Title = "Latest Books" },
                Links = [new(FormatUrl("books/latest", apiKey), OpdsV2MimeTypes.OpdsJson, "self")],
                Publications = latestBooksFeed.Publications.Take(10).ToList()
            });
        }

        return feed;
    }

    public async Task<OpdsV2Feed> SearchFeedAsync(AuthUser user, string query, string? apiKey, CancellationToken ct = default)
    {
        var search = query?.Trim() ?? string.Empty;
        var feed = new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = $"Search Results: {search}",
                Modified = DateTimeOffset.UtcNow.ToString("O")
            },
            Links =
            [
                new(FormatUrl($"search?query={Uri.EscapeDataString(search)}", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ]
        };

        if (string.IsNullOrWhiteSpace(search)) return feed;

        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        where = $"{where} AND (m.\"Name\" LIKE @SearchPattern OR mm.\"Title\" LIKE @SearchPattern)";
        parameters.Add("SearchPattern", $"%{search}%");

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year",
                   s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT {PageSize};
            """;

        var books = (await conn.QueryAsync<Media, MediaMetadata, Series, Media>(
            sql,
            (media, meta, series) =>
            {
                media.Metadata = meta;
                media.Series = series;
                return media;
            },
            parameters,
            splitOn: "MediaId,Id")).ToList();

        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        feed.Publications = books.Select(b => ToPublication(b, apiKey, epubTotals.TotalFor(b.Id))).ToList();
        return feed;
    }

    public async Task<OpdsV2Feed> GetLibrariesFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");

        var sql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {where}
            ORDER BY l."Name" ASC;
            """;

        var libraries = (await conn.QueryAsync<Library>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "Libraries",
                NumberOfItems = libraries.Count
            },
            Links =
            [
                new(FormatUrl("libraries", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
                new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
            ],
            Navigation = libraries.Select(l => new OpdsV2Link(FormatUrl($"libraries/{l.Id}", apiKey), OpdsV2MimeTypes.OpdsJson)
            {
                Properties = new() { [TitleKey] = l.Name }
            }).ToList()
        };
    }

    public async Task<OpdsV2Feed> GetSeriesFeedAsync(AuthUser user, int page, string? apiKey, CancellationToken ct = default)
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

        var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
        var links = new List<OpdsV2Link>
        {
            new(FormatUrl($"series?page={page}", apiKey), OpdsV2MimeTypes.OpdsJson, "self"),
            new(FormatUrl(CatalogEndpoint, apiKey), OpdsV2MimeTypes.OpdsJson, StartRel)
        };

        if (page > 0)
        {
            links.Add(new(FormatUrl($"series?page={page - 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "previous"));
        }
        if (page < totalPages - 1)
        {
            links.Add(new(FormatUrl($"series?page={page + 1}", apiKey), OpdsV2MimeTypes.OpdsJson, "next"));
        }

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = "All Series",
                NumberOfItems = totalCount,
                ItemsPerPage = PageSize,
                CurrentPage = page
            },
            Links = links,
            Navigation = seriesList.Select(s => new OpdsV2Link(FormatUrl($"series/{s.Id}", apiKey), OpdsV2MimeTypes.OpdsJson)
            {
                Properties = new() { [TitleKey] = s.Metadata?.Title ?? s.Name }
            }).ToList()
        };
    }

    public async Task<OpdsV2Feed?> GetLibrarySeriesFeedAsync(AuthUser user, string libraryId, int page, string? apiKey, CancellationToken ct = default)
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
            return null;
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

        return new OpdsV2Feed
        {
            Metadata = new OpdsV2Metadata
            {
                Title = library.Name,
                NumberOfItems = totalCount,
                ItemsPerPage = PageSize,
                CurrentPage = page
            },
            Links = BuildPagingLinks($"libraries/{libraryId}", page, totalCount, apiKey),
            Navigation = seriesList.Select(s => new OpdsV2Link(FormatUrl($"series/{s.Id}", apiKey), OpdsV2MimeTypes.OpdsJson)
            {
                Properties = new() { [TitleKey] = s.Metadata?.Title ?? s.Name }
            }).ToList()
        };
    }

    public async Task<OpdsV2Feed?> GetSeriesBooksFeedAsync(AuthUser user, string seriesId, int page, string? apiKey, CancellationToken ct = default)
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
            return null;
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
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year",
                   s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
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

        var books = (await conn.QueryAsync<Media, MediaMetadata, Series, Media>(
            pagedSql,
            (media, meta, ser) =>
            {
                media.Metadata = meta;
                media.Series = ser;
                return media;
            },
            mParams,
            splitOn: "MediaId,Id")).ToList();

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
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year",
                   s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
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

        var books = (await conn.QueryAsync<Media, MediaMetadata, Series, Media>(
            pagedSql,
            (media, meta, series) =>
            {
                media.Metadata = meta;
                media.Series = series;
                return media;
            },
            parameters,
            splitOn: "MediaId,Id")).ToList();

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

        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("MediaIds", mediaIds);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year",
                   s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" IN @MediaIds AND {where};
            """;

        var books = mediaIds.Count == 0
            ? []
            : (await conn.QueryAsync<Media, MediaMetadata, Series, Media>(
                sql,
                (media, meta, series) =>
                {
                    media.Metadata = meta;
                    media.Series = series;
                    return media;
                },
                parameters,
                splitOn: "MediaId,Id")).ToList();

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
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("Id", bookId);

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year",
                   s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var books = await conn.QueryAsync<Media, MediaMetadata, Series, Media>(
            sql,
            (media, meta, series) =>
            {
                media.Metadata = meta;
                media.Series = series;
                return media;
            },
            parameters,
            splitOn: "MediaId,Id");

        var book = books.FirstOrDefault();
        if (book == null) return null;

        var progress = await _progress.ResolveAsync(book, session: null, ct);
        return ToPublication(book, apiKey, progress.TotalPages, includeReadingOrder: true);
    }

    public async Task<OpdsV2Progression?> GetProgressionAsync(AuthUser user, string bookId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var session = await conn.QueryFirstOrDefaultAsync<ReadingSession>(
            new CommandDefinition(
                """
                SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                       "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                       "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                       "RenderedPage", "RenderedTotalPages", "RenderedProfileKey"
                FROM "ReadingSessions"
                WHERE "MediaId" = @BookId AND "UserId" = @UserId
                ORDER BY "Id" DESC
                LIMIT 1;
                """,
                new { BookId = bookId, UserId = user.Id },
                cancellationToken: ct));

        if (session == null) return null;

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

        // Las paginas que el lector va a pedir, no las del OPF: en un EPUB son las del render.
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
