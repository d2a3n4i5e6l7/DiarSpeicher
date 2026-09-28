using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.GraphQL;

public class Query
{
    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public async Task<IEnumerable<Library>> GetLibraries(
        [Service] ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(auth.Require(), "l");
        var sql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Emoji", l."Name", l."Path", l."Status", l."ThumbnailPath", l."UpdatedAt"
            FROM "Libraries" l
            WHERE {where}
            ORDER BY l."Name" ASC;
            """;
        return await conn.QueryAsync<Library>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public async Task<IEnumerable<Series>> GetSeries(
        [Service] ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        string? libraryId = null,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildSeriesVisibilityFilter(auth.Require(), "s", "sm");
        if (libraryId != null)
        {
            where = $"{where} AND s.\"LibraryId\" = @LibraryId";
            parameters.Add("LibraryId", libraryId);
        }

        var sql = $"""
            SELECT s."Id", s."CreatedAt", s."DeletedAt", s."Description", s."LibraryId", s."Name", s."Path", s."Status", s."UpdatedAt",
                   sm."SeriesId", sm."AgeRating", sm."Booktype", sm."Comicid", sm."ComicImage", sm."Genres", sm."Links", sm."MetaType", sm."PublicationRun", sm."Publisher", sm."Status", sm."Summary", sm."Title", sm."TotalIssues", sm."Volume", sm."Writers", sm."Year"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY s."Name" ASC;
            """;

        return await conn.QueryAsync<Series, SeriesMetadata, Series>(
            sql,
            (s, sm) =>
            {
                s.Metadata = sm;
                return s;
            },
            parameters,
            splitOn: "SeriesId");
    }

    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public async Task<IEnumerable<Media>> GetMedia(
        [Service] ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        string? seriesId = null,
        CancellationToken ct = default)
    {
        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(auth.Require(), "m", "mm", "s", "sm");
        if (seriesId != null)
        {
            where = $"{where} AND m.\"SeriesId\" = @SeriesId";
            parameters.Add("SeriesId", seriesId);
        }

        var sql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt",
                   mm."MediaId", mm."AgeRating", mm."Genres", mm."Number", mm."Publisher", mm."Summary", mm."Title", mm."Writers", mm."Year"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."SortName" ASC, m."Name" ASC;
            """;

        return await conn.QueryAsync<Media, MediaMetadata, Media>(
            sql,
            (media, meta) =>
            {
                media.Metadata = meta;
                return media;
            },
            parameters,
            splitOn: "MediaId");
    }

    /// <summary>
    /// Books the caller is part-way through. Scoped to the caller's own sessions and to
    /// media they may see, so an excluded library never surfaces through reading history.
    /// </summary>
    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public async Task<IEnumerable<ReadingSession>> GetReadingSessions(
        [Service] ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        CancellationToken ct = default)
    {
        var user = auth.Require();
        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("UserId", user.Id);

        var sql = $"""
            SELECT rs."Id", rs."CreatedAt", rs."ElapsedSeconds", rs."EndPage", rs."EndPercentage",
                   rs."KoreaderProgress", rs."MediaId", rs."Notes", rs."ReadthroughNumber", rs."SessionDate",
                   rs."StartPage", rs."StartPercentage", rs."Status", rs."UpdatedAt", rs."UserId",
                   rs."RenderedPage", rs."RenderedTotalPages", rs."RenderedProfileKey"
            FROM "ReadingSessions" rs
            JOIN "Media" m ON rs."MediaId" = m."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE rs."UserId" = @UserId AND {where}
            ORDER BY rs."Id" DESC;
            """;

        return await conn.QueryAsync<ReadingSession>(new CommandDefinition(sql, parameters, cancellationToken: ct));
    }

    public ServerConfig GetServerConfig(IOptions<StorageOptions> storageOptions)
    {
        var upload = storageOptions.Value.Upload;

        return new ServerConfig
        {
            Version = "1.0.0",
            UploadEnabled = upload.EnableUpload,
            MaxFileUploadSize = upload.MaxFileUploadSize,
            AllowedExtensions = [.. upload.ResolveAllowedExtensions()]
        };
    }
}

public class ServerConfig
{
    public string Version { get; init; } = string.Empty;
    public bool UploadEnabled { get; init; }
    public long MaxFileUploadSize { get; init; }
    public List<string> AllowedExtensions { get; init; } = [];
}

public static class GraphQLConstants
{
    public const string AuthUserKey = "AuthUser";
}
