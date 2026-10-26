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
                   "RenderedPage", "RenderedTotalPages", "RenderedProfileKey", "RenderedStatus"
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

    /// <summary>
    /// An EPUB keeps two independent progresses in one row: the percentage the e-reader protocols
    /// write and the rendered page the image protocols write. Each listing only sees its own.
    /// </summary>
    public static async Task<List<ReadingSession>> GetKeepReadingSessionsAsync(
        this System.Data.Common.DbConnection conn,
        string userId,
        bool imageTrack = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            SELECT "Id", "CreatedAt", "ElapsedSeconds", "EndPage", "EndPercentage",
                   "KoreaderProgress", "MediaId", "Notes", "ReadthroughNumber", "SessionDate",
                   "StartPage", "StartPercentage", "Status", "UpdatedAt", "UserId",
                   "RenderedPage", "RenderedTotalPages", "RenderedProfileKey", "RenderedStatus"
            FROM (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY "MediaId" ORDER BY COALESCE("UpdatedAt", "CreatedAt") DESC, "Id" DESC
                ) AS "rn"
                FROM "ReadingSessions"
                WHERE "UserId" = @UserId AND (
                    CASE WHEN @ImageTrack = 0 THEN
                        "Status" = @Status AND ("EndPercentage" IS NOT NULL OR "RenderedPage" IS NULL)
                    ELSE
                        CASE WHEN "RenderedPage" IS NOT NULL THEN "RenderedStatus"
                             WHEN "MediaId" IN (SELECT "Id" FROM "Media" WHERE lower(trim("Extension", '.')) = 'epub' OR lower("Path") LIKE '%.epub') THEN NULL
                             ELSE "Status" END = @Status
                    END)
            )
            WHERE "rn" = 1
            ORDER BY COALESCE("UpdatedAt", "CreatedAt") DESC;
            """;

        var sessions = (await conn.QueryAsync<ReadingSession>(sql, new { UserId = userId, Status = nameof(ReadingStatus.Reading), ImageTrack = imageTrack ? 1 : 0 })).AsRowList();

        return sessions;
    }
}
