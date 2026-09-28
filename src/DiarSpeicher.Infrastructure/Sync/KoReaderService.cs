using System.Data;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Sync;

public sealed class KoReaderService : IKoReaderService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<KoReaderService> _logger;

    public KoReaderService(ISqliteConnectionFactory connectionFactory, ILogger<KoReaderService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public Task<KoReaderAuthResponse> CheckAuthorizedAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new KoReaderAuthResponse { Authorized = "OK" });
    }

    public async Task<KoReaderProgressResponse> GetProgressAsync(AuthUser user, string koreaderHash, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("KoreaderHash", koreaderHash);

        var mediaSql = $"""
            SELECT m."Id"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."KoreaderHash" = @KoreaderHash AND {where}
            LIMIT 1;
            """;

        var mediaId = await conn.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(mediaSql, parameters, cancellationToken: ct));

        if (mediaId == null)
        {
            _logger.LogDebug("Media with KOReader hash {Hash} not found for user {UserId}", koreaderHash, user.Id);
            return new KoReaderProgressResponse { Document = koreaderHash };
        }

        const string sessionSql = """
            SELECT "Id", "CreatedAt", "UpdatedAt", "EndPercentage", "KoreaderProgress", "EndPage"
            FROM "ReadingSessions"
            WHERE "UserId" = @UserId AND "MediaId" = @MediaId
            ORDER BY "Id" DESC
            LIMIT 1;
            """;

        var session = await conn.QueryFirstOrDefaultAsync<ReadingSession>(
            new CommandDefinition(sessionSql, new { UserId = user.Id, MediaId = mediaId }, cancellationToken: ct));

        if (session == null)
        {
            return new KoReaderProgressResponse { Document = koreaderHash };
        }

        var timestamp = (ulong)(session.UpdatedAt ?? session.CreatedAt).ToUnixTimeMilliseconds();

        return new KoReaderProgressResponse
        {
            Document = koreaderHash,
            Percentage = session.EndPercentage.HasValue ? (float)session.EndPercentage.Value : null,
            Progress = session.KoreaderProgress ?? session.EndPage?.ToString(),
            Timestamp = timestamp
        };
    }

    public async Task<KoReaderPutProgressResponse> UpdateProgressAsync(AuthUser user, KoReaderProgressInput input, CancellationToken ct = default)
    {
        if (input.Percentage < 0.0f || input.Percentage > 1.0f)
        {
            throw new ArgumentException("Invalid percentage", nameof(input));
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        parameters.Add("Document", input.Document);

        var mediaSql = $"""
            SELECT m."Id"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."KoreaderHash" = @Document AND {where}
            LIMIT 1;
            """;

        var mediaId = await conn.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(mediaSql, parameters, cancellationToken: ct));

        if (mediaId == null)
        {
            throw new KeyNotFoundException($"Media with KOReader hash '{input.Document}' not found");
        }

        const string checkUserSql = @"SELECT 1 FROM ""Users"" WHERE ""Id"" = @UserId LIMIT 1;";
        var userExists = await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition(checkUserSql, new { UserId = user.Id }, cancellationToken: ct));

        if (userExists is null)
        {
            throw new UnauthorizedAccessException("Reading progress requires a registered user");
        }

        const string sessionSql = """
            SELECT "Id", "CreatedAt", "UpdatedAt", "EndPercentage", "KoreaderProgress", "EndPage", "StartPage", "StartPercentage", "Status"
            FROM "ReadingSessions"
            WHERE "UserId" = @UserId AND "MediaId" = @MediaId
            ORDER BY "Id" DESC
            LIMIT 1;
            """;

        var session = await conn.QueryFirstOrDefaultAsync<ReadingSession>(
            new CommandDefinition(sessionSql, new { UserId = user.Id, MediaId = mediaId }, cancellationToken: ct));

        var now = DateTimeOffset.UtcNow;
        var endPercentage = (decimal)input.Percentage;
        var koreaderProgress = input.Progress;
        int? endPage = int.TryParse(input.Progress, out var p) ? p : session?.EndPage;
        var status = input.Percentage >= 1.0f ? ReadingStatus.Finished : ReadingStatus.Reading;

        if (session == null)
        {
            const string insertSessionSql = """
                INSERT INTO "ReadingSessions" (
                    "UserId", "MediaId", "SessionDate", "StartPage", "EndPage", "StartPercentage", "EndPercentage",
                    "KoreaderProgress", "Status", "CreatedAt", "UpdatedAt"
                ) VALUES (
                    @UserId, @MediaId, @SessionDate, 1, @EndPage, 0, @EndPercentage,
                    @KoreaderProgress, @Status, @CreatedAt, @UpdatedAt
                );
                """;

            await conn.ExecuteAsync(new CommandDefinition(insertSessionSql, new
            {
                UserId = user.Id,
                MediaId = mediaId,
                SessionDate = DateOnly.FromDateTime(now.UtcDateTime),
                EndPage = endPage,
                EndPercentage = endPercentage,
                KoreaderProgress = koreaderProgress,
                Status = status.ToString(),
                CreatedAt = now,
                UpdatedAt = now
            }, cancellationToken: ct));
        }
        else
        {
            const string updateSessionSql = """
                UPDATE "ReadingSessions"
                SET "EndPercentage" = @EndPercentage,
                    "KoreaderProgress" = @KoreaderProgress,
                    "EndPage" = @EndPage,
                    "Status" = @Status,
                    "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id;
                """;

            await conn.ExecuteAsync(new CommandDefinition(updateSessionSql, new
            {
                EndPercentage = endPercentage,
                KoreaderProgress = koreaderProgress,
                EndPage = endPage,
                Status = status.ToString(),
                UpdatedAt = now,
                Id = session.Id
            }, cancellationToken: ct));
        }

        return new KoReaderPutProgressResponse
        {
            Document = input.Document,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
