using Dapper;

namespace DiarSpeicher.Infrastructure.Data.Extensions;

public static class ReadingSessionSqlQueries
{

    public static async Task<Dictionary<string, ReadingSession>> GetLatestSessionsPerMediaAsync(
        this System.Data.Common.DbConnection conn,
        string? userId,
        IReadOnlyCollection<string> mediaIds,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(userId) || mediaIds.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                   "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                   "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                   "RenderedPage", "RenderedTotalPages", "RenderedProfileKey"
            FROM (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY "MediaId" ORDER BY "Id" DESC) AS "rn"
                FROM "ReadingSessions"
                WHERE "UserId" = @UserId
            )
            WHERE "rn" = 1 AND "MediaId" IN (SELECT value FROM json_each(@MediaIds));
            """;

        var sessions = (await conn.QueryAsync<ReadingSession>(sql, new { UserId = userId, MediaIds = SqlKeys.ToJson(mediaIds) })).AsRowList();

        return sessions.ToDictionary(s => s.MediaId);
    }

    public static async Task<List<ReadingSession>> GetKeepReadingSessionsAsync(
        this System.Data.Common.DbConnection conn,
        string userId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                   "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                   "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                   "RenderedPage", "RenderedTotalPages", "RenderedProfileKey"
            FROM (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY "MediaId" ORDER BY COALESCE("UpdatedAt", "CreatedAt") DESC, "Id" DESC
                ) AS "rn"
                FROM "ReadingSessions"
                WHERE "UserId" = @UserId AND "Status" = @Status
            )
            WHERE "rn" = 1
            ORDER BY COALESCE("UpdatedAt", "CreatedAt") DESC;
            """;

        var sessions = (await conn.QueryAsync<ReadingSession>(sql, new { UserId = userId, Status = nameof(ReadingStatus.Reading) })).AsRowList();

        return sessions;
    }
}
