using System.Data.Common;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;

namespace DiarSpeicher.Infrastructure.Komga;

internal sealed record SeriesReadCounts(int Read, int InProgress);

internal sealed record SeriesReadCountRow(string SeriesId, int Read, int InProgress);

internal sealed record NumberedBook(Media Media, float NumberSort);

public partial class KomgaService
{
    private static async Task<Dictionary<string, SeriesReadCounts>> LoadSeriesReadCountsAsync(
        DbConnection conn,
        string userId,
        IEnumerable<string> seriesIds)
    {
        const string sql = """
            WITH latest AS (
                SELECT rs."MediaId",
                       CASE WHEN lower(trim(md."Extension", '.')) = 'epub' OR lower(md."Path") LIKE '%.epub'
                            THEN rs."RenderedStatus" ELSE rs."Status" END AS "Status",
                       ROW_NUMBER() OVER (PARTITION BY rs."MediaId" ORDER BY rs."Id" DESC) AS rn
                FROM "ReadingSessions" rs
                INNER JOIN "Media" md ON md."Id" = rs."MediaId"
                WHERE rs."UserId" = @UserId
            )
            SELECT m."SeriesId" AS "SeriesId",
                   SUM(CASE WHEN latest."Status" = 'Finished' THEN 1 ELSE 0 END) AS "Read",
                   SUM(CASE WHEN latest."Status" = 'Reading' THEN 1 ELSE 0 END) AS "InProgress"
            FROM latest
            INNER JOIN "Media" m ON m."Id" = latest."MediaId"
            WHERE latest.rn = 1
              AND m."DeletedAt" IS NULL
              AND m."SeriesId" IN (SELECT value FROM json_each(@SeriesKeys))
            GROUP BY m."SeriesId";
            """;

        var args = new CatalogQueryArgs { UserId = userId, SeriesKeys = SqlKeys.ToJson(seriesIds) };
        var rows = await conn.QueryAsync<SeriesReadCountRow>(sql, args);
        return rows.ToDictionary(r => r.SeriesId, r => new SeriesReadCounts(r.Read, r.InProgress), StringComparer.Ordinal);
    }

    /// <summary>
    /// Null when the series does not exist or is hidden from the user. Books without a number in
    /// their metadata take their position, so a series of unnumbered volumes still has an order.
    /// </summary>
    private static async Task<List<NumberedBook>?> LoadNumberedBooksAsync(DbConnection conn, AuthUser user, string seriesId)
    {
        var args = CatalogSqlFilters.ArgsFor(user);
        args.Id = seriesId;
        args.SeriesId = seriesId;

        const string visibleSql = $"""
            SELECT s."Id"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."Id" = @Id AND {CatalogSqlFilters.SeriesVisible}
            LIMIT 1;
            """;

        if (await conn.QueryFirstOrDefaultAsync<string>(visibleSql, args) is null) return null;

        const string booksSql = $"""
            SELECT m."Id", m."CreatedAt", m."DeletedAt", m."Extension", m."Status", m."Hash", m."KoreaderHash", m."Name", m."Pages", m."Path", m."SeriesId", m."Size", m."SortName", m."ThumbnailPath", m."ThumbnailMeta", m."ModifiedAt", m."UpdatedAt"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."SeriesId" = @SeriesId AND {CatalogSqlFilters.MediaVisible}
            ORDER BY m."SortName" ASC, m."Name" ASC;
            """;

        var books = (await conn.QueryAsync<Media>(booksSql, args).WithMetadataAsync(conn)).AsRowList();
        return [.. books
            .Select((book, index) => new NumberedBook(book, book.Metadata?.Number is decimal number ? (float)number : index + 1))
            .OrderBy(b => b.NumberSort)];
    }

    public async Task<KomgaTachiyomiProgressDto?> GetTachiyomiProgressAsync(AuthUser user, string seriesId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var books = await LoadNumberedBooksAsync(conn, user, seriesId);
        if (books is null) return null;

        var sessions = await conn.GetLatestSessionsPerMediaAsync(user.Id, books.Select(b => b.Media.Id).ToList(), ct);
        var read = books.Count(b => IsFinished(sessions, b.Media));
        var inProgress = books.Count(b => sessions.TryGetValue(b.Media.Id, out var s) && ImageStatus(b.Media, s) == nameof(ReadingStatus.Reading));
        var lastRead = books.TakeWhile(b => IsFinished(sessions, b.Media)).Select(b => b.NumberSort).LastOrDefault();

        return new KomgaTachiyomiProgressDto
        {
            BooksCount = books.Count,
            BooksReadCount = read,
            BooksInProgressCount = inProgress,
            BooksUnreadCount = books.Count - read - inProgress,
            LastReadContinuousNumberSort = lastRead,
            MaxNumberSort = books.Count == 0 ? 0 : books.Max(b => b.NumberSort)
        };
    }

    /// <summary>
    /// Everything up to the given number is marked read and everything after it unread, which is
    /// how Mihon expresses "I am at chapter N".
    /// </summary>
    public async Task<bool> SetTachiyomiProgressAsync(AuthUser user, string seriesId, float lastReadNumberSort, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateConnectionAsync(ct);
        var books = await LoadNumberedBooksAsync(conn, user, seriesId);
        if (books is null) return false;

        foreach (var book in books)
        {
            if (book.NumberSort <= lastReadNumberSort)
            {
                await _progress.RecordAsync(user, book.Media, new ReadingProgressUpdate(int.MaxValue, true), ct);
            }
            else
            {
                await DeleteReadProgressAsync(user, book.Media.Id, ct);
            }
        }

        return true;
    }

    private static string? ImageStatus(Media book, ReadingSession session) =>
        EpubPageMapStore.IsEpub(book) ? session.RenderedStatus : session.Status.ToString();

    private static bool IsFinished(Dictionary<string, ReadingSession> sessions, Media book) =>
        sessions.TryGetValue(book.Id, out var session) && ImageStatus(book, session) == nameof(ReadingStatus.Finished);
}
