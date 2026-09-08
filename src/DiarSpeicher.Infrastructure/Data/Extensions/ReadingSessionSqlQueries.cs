using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

public static class ReadingSessionSqlQueries
{
    public static async Task<Dictionary<string, ReadingSession>> GetLatestSessionsPerMediaAsync(
        this DiarSpeicherDbContext db,
        string? userId,
        IReadOnlyCollection<string> mediaIds,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || mediaIds.Count == 0)
        {
            return [];
        }

        var sessions = await db.ReadingSessions
            .FromSql(
                $"""
                SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                       "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                       "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId"
                FROM (
                    SELECT *, ROW_NUMBER() OVER (PARTITION BY "MediaId" ORDER BY "Id" DESC) AS "rn"
                    FROM "ReadingSessions"
                    WHERE "UserId" = {userId}
                )
                WHERE "rn" = 1
                """)
            .Where(s => mediaIds.Contains(s.MediaId))
            .AsNoTracking()
            .ToListAsync(ct);

        return sessions.ToDictionary(s => s.MediaId);
    }

    public static async Task<List<ReadingSession>> GetKeepReadingSessionsAsync(
        this DiarSpeicherDbContext db,
        string userId,
        CancellationToken ct = default) =>
        await db.ReadingSessions
            .FromSql(
                $"""
                SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                       "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                       "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId"
                FROM (
                    SELECT *, ROW_NUMBER() OVER (
                        PARTITION BY "MediaId" ORDER BY COALESCE("UpdatedAt", "CreatedAt") DESC, "Id" DESC
                    ) AS "rn"
                    FROM "ReadingSessions"
                    WHERE "UserId" = {userId} AND "Status" = {nameof(ReadingStatus.Reading)}
                )
                WHERE "rn" = 1
                ORDER BY COALESCE("UpdatedAt", "CreatedAt") DESC
                """)
            .AsNoTracking()
            .ToListAsync(ct);
}
