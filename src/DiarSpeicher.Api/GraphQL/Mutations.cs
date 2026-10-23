using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.GraphQL;

public class Mutation
{
    public async Task<Library> CreateLibrary(
        ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        IOptions<LibraryRootsOptions> libraryRoots,
        CreateLibraryInput input,
        CancellationToken ct)
    {
        if (!libraryRoots.Value.TryResolve(input.Path, out var fullPath))
        {
            throw new GraphQLException($"The path '{input.Path}' is outside the allowed library roots.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new GraphQLException($"The path '{input.Path}' does not exist.");
        }

        var library = new Library
        {
            Id = Guid.NewGuid().ToString(),
            Name = input.Name,
            Path = fullPath,
            Description = input.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            Config = new LibraryConfig()
        };

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("LibraryPattern", "LibraryType", "DefaultReadingDir", "DefaultReadingMode", "DefaultLibraryViewMode")
            VALUES (@LibraryPattern, @LibraryType, @DefaultReadingDir, @DefaultReadingMode, @DefaultLibraryViewMode);
            SELECT last_insert_rowid();
            """;

        library.Config.Id = await conn.ExecuteScalarAsync<int>(insertConfigSql, new
            {
                LibraryPattern = library.Config.LibraryPattern.ToString(),
                LibraryType = library.Config.LibraryType.ToString(),
                DefaultReadingDir = library.Config.DefaultReadingDir.ToString(),
                DefaultReadingMode = library.Config.DefaultReadingMode.ToString(),
                DefaultLibraryViewMode = library.Config.DefaultLibraryViewMode.ToString()
            }, tx);

        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Description", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, @Name, @Path, @Description, @Status, @ConfigId, @CreatedAt);
            """;

        await conn.ExecuteAsync(insertLibrarySql, new { Id = library.Id, Name = library.Name, Path = library.Path, Description = library.Description, Status = library.Status.ToString(), ConfigId = library.Config.Id, CreatedAt = library.CreatedAt }, tx);

        await tx.CommitAsync(ct);

        return library;
    }

    public static async Task<Library> EditLibrary(
        ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        EditLibraryInput input,
        CancellationToken ct)
    {
        var user = RequireServerOwner(auth);

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        parameters.Add("Id", input.Id);

        var selectSql = $"""
            SELECT l."Id", l."CreatedAt", l."Description", l."Emoji", l."Name", l."Path", l."Status", l."ThumbnailPath", l."UpdatedAt"
            FROM "Libraries" l
            WHERE l."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var library = await conn.QueryFirstOrDefaultAsync<Library>(selectSql, parameters)
            ?? throw new GraphQLException($"Library '{input.Id}' not found.");

        if (input.Name != null) library.Name = input.Name;
        if (input.Description != null) library.Description = input.Description;
        if (input.Emoji != null) library.Emoji = input.Emoji;

        library.UpdatedAt = DateTimeOffset.UtcNow;

        const string updateSql = """
            UPDATE "Libraries"
            SET "Name" = @Name, "Description" = @Description, "Emoji" = @Emoji, "UpdatedAt" = @UpdatedAt
            WHERE "Id" = @Id;
            """;

        await conn.ExecuteAsync(updateSql, new { Name = library.Name, Description = library.Description, Emoji = library.Emoji, UpdatedAt = library.UpdatedAt, Id = library.Id });

        return library;
    }

    public static async Task<bool> DeleteLibrary(
        ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        string id,
        CancellationToken ct)
    {
        var user = RequireServerOwner(auth);

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        parameters.Add("Id", id);

        var checkSql = $"""SELECT 1 FROM "Libraries" l WHERE l."Id" = @Id AND {where} LIMIT 1;""";
        var exists = await conn.ExecuteScalarAsync<int?>(checkSql, parameters);
        if (!exists.HasValue) return false;

        const string deleteSql = """DELETE FROM "Libraries" WHERE "Id" = @Id;""";
        await conn.ExecuteAsync(deleteSql, new { Id = id });

        return true;
    }

    public async Task<ScanJob> ScanLibrary(
        ISqliteConnectionFactory connectionFactory,
        IScannerQueue queue,
        AuthUserResolver auth,
        string id,
        CancellationToken ct)
    {
        var user = auth.Require();
        if (!user.HasPermission(Permissions.ScanLibrary))
        {
            throw new GraphQLException("This account is not allowed to scan libraries.");
        }

        await using var conn = await connectionFactory.CreateConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildLibraryVisibilityFilter(user, "l");
        parameters.Add("Id", id);

        var checkSql = $"""SELECT 1 FROM "Libraries" l WHERE l."Id" = @Id AND {where} LIMIT 1;""";
        var exists = await conn.ExecuteScalarAsync<int?>(checkSql, parameters);
        if (!exists.HasValue)
        {
            throw new GraphQLException($"Library '{id}' not found.");
        }

        await queue.QueueScanAsync(new ScanRequest(id), ct);

        return new ScanJob { JobId = id, LibraryId = id, Queued = true };
    }

    public async Task<ReadingSession> UpdateReadingProgress(
        ISqliteConnectionFactory connectionFactory,
        AuthUserResolver auth,
        UpdateReadingProgressInput input,
        CancellationToken ct)
    {
        var user = auth.Require();
        await using var conn = await connectionFactory.CreateConnectionAsync(ct);

        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("Id", input.MediaId);

        var mediaSql = $"""
            SELECT m."Id"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {where}
            LIMIT 1;
            """;

        var mediaId = await conn.QueryFirstOrDefaultAsync<string>(mediaSql, parameters)
            ?? throw new GraphQLException($"Media '{input.MediaId}' not found.");

        const string sessionSql = """
            SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                   "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                   "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                   "RenderedPage", "RenderedTotalPages", "RenderedProfileKey"
            FROM "ReadingSessions"
            WHERE "UserId" = @UserId AND "MediaId" = @MediaId
            ORDER BY "Id" DESC
            LIMIT 1;
            """;

        var session = await conn.QueryFirstOrDefaultAsync<ReadingSession>(
            sessionSql, new { UserId = user.Id, MediaId = mediaId });

        var now = DateTimeOffset.UtcNow;
        if (session == null)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = mediaId,
                CreatedAt = now,
                UpdatedAt = now,
                Status = ReadingStatus.Reading
            };

            if (input.Page.HasValue) session.EndPage = input.Page;
            if (input.Percentage.HasValue) session.EndPercentage = input.Percentage;
            if (input.IsCompleted == true) session.Status = ReadingStatus.Finished;

            const string insertSql = """
                INSERT INTO "ReadingSessions" ("UserId", "MediaId", "SessionDate", "EndPage", "EndPercentage", "Status", "CreatedAt", "UpdatedAt")
                VALUES (@UserId, @MediaId, @SessionDate, @EndPage, @EndPercentage, @Status, @CreatedAt, @UpdatedAt);
                """;

            await conn.ExecuteAsync(insertSql, new
            {
                session.UserId,
                session.MediaId,
                session.SessionDate,
                session.EndPage,
                session.EndPercentage,
                Status = session.Status.ToString(),
                session.CreatedAt,
                session.UpdatedAt
            });
        }
        else
        {
            if (input.Page.HasValue) session.EndPage = input.Page;
            if (input.Percentage.HasValue) session.EndPercentage = input.Percentage;
            if (input.IsCompleted == true) session.Status = ReadingStatus.Finished;
            session.UpdatedAt = now;

            const string updateSql = """
                UPDATE "ReadingSessions"
                SET "EndPage" = @EndPage, "EndPercentage" = @EndPercentage, "Status" = @Status, "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id;
                """;

            await conn.ExecuteAsync(updateSql, new
            {
                session.EndPage,
                session.EndPercentage,
                Status = session.Status.ToString(),
                session.UpdatedAt,
                session.Id
            });
        }

        return session;
    }

    private static AuthUser RequireServerOwner(AuthUserResolver auth)
    {
        var user = auth.Require();
        if (!user.IsServerOwner)
        {
            throw new GraphQLException("Only the server owner may perform this operation.");
        }

        return user;
    }
}

public record CreateLibraryInput(string Name, string Path, string? Description);

public record EditLibraryInput(string Id, string? Name, string? Description, string? Emoji);

public record UpdateReadingProgressInput(string MediaId, int? Page, decimal? Percentage, bool? IsCompleted);

public class ScanJob
{
    public string JobId { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
    public bool Queued { get; init; }
}
