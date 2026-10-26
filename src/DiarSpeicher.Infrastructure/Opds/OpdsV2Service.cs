using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Gateway;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Opds;

public partial class OpdsV2Service : IOpdsV2Service
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
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.SearchPattern = $"%{search}%";

        parameters.Take = PageSize;

        const string sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name",
                   m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta",
                   m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND {CatalogSqlFilters.MediaSearch}
            ORDER BY m."SortName" ASC, m."Name" ASC
            LIMIT @Take;
            """;

        var books = (await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn).WithSeriesAsync(conn)).ToList();

        var epubTotals = await _progress.EpubTotalsAsync(books, ct);
        feed.Publications = books.Select(b => ToPublication(b, apiKey, epubTotals.TotalFor(b.Id))).ToList();
        return feed;
    }

    public async Task<OpdsV2Feed> GetLibrariesFeedAsync(AuthUser user, string? apiKey, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        const string sql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Name", l."Path", l."Status", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {CatalogSqlFilters.LibraryVisible}
            ORDER BY l."Name" ASC;
            """;

        var libraries = (await conn.QueryAsync<Library>(sql, parameters)).ToList();

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
            ORDER BY s."Name" ASC
            LIMIT @Take OFFSET @Skip;
            """;

        parameters.Take = PageSize;
        parameters.Skip = page * PageSize;

        var seriesList = (await conn.QueryAsync<Series>(pagedSql, parameters).WithMetadataAsync(conn)).ToList();

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
            return null;
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
}
