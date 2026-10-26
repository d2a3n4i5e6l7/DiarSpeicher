using System.Data;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Reading;

public class ReadingProgress : IReadingProgress
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IEpubPageMapStore _pageMaps;
    private readonly IEpubProfileProvider _profiles;
    private readonly ILogger<ReadingProgress> _logger;

    public ReadingProgress(
        ISqliteConnectionFactory connectionFactory,
        IEpubPageMapStore pageMaps,
        IEpubProfileProvider profiles,
        ILogger<ReadingProgress> logger)
    {
        _connectionFactory = connectionFactory;
        _pageMaps = pageMaps;
        _profiles = profiles;
        _logger = logger;
    }

    public static ReadingProgressView ViewFromSession(Media book, ReadingSession? session, int? currentTotal = null)
    {
        if (!EpubPageMapStore.IsEpub(book))
        {
            return new ReadingProgressView(session?.EndPage, Math.Max(1, book.Pages), false);
        }

        return ViewFromEpubSession(book, session, currentTotal);
    }

    private static ReadingProgressView ViewFromEpubSession(Media book, ReadingSession? session, int? currentTotal)
    {
        var storedTotal = session?.RenderedTotalPages ?? 0;
        var fallbackTotal = storedTotal > 0 ? storedTotal : book.Pages;
        var total = currentTotal is > 0 ? currentTotal.Value : Math.Max(1, fallbackTotal);

        if (session?.RenderedPage is not int storedPage)
        {
            return new ReadingProgressView(session?.EndPage, total, false);
        }

        if (storedTotal <= 0 || storedTotal == total)
        {
            return new ReadingProgressView(Math.Clamp(storedPage, 1, total), total, false);
        }

        return new ReadingProgressView(Rescale(storedPage, storedTotal, total), total, true);
    }

    public async Task<bool> RecordAsync(
        AuthUser user,
        Media book,
        ReadingProgressUpdate update,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.Id)) return false;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        const string checkUserSql = @"SELECT 1 FROM ""Users"" WHERE ""Id"" = @UserId LIMIT 1;";
        var userExists = await conn.ExecuteScalarAsync<int?>(checkUserSql, new { UserId = user.Id });
        if (userExists is null) return false;

        var isEpub = EpubPageMapStore.IsEpub(book);
        var profileKey = isEpub ? await CurrentProfileKeyAsync(ct) : null;
        var total = await ResolveTotalAsync(book, ct);
        var page = Math.Clamp(update.Page, 1, Math.Max(1, total));

        const string selectSessionSql = """
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
            selectSessionSql, new { UserId = user.Id, MediaId = book.Id });

        var isNew = session == null;
        if (isNew)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = book.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        ApplyProgressUpdate(session!, isEpub, page, total, profileKey, update);

        if (isNew)
        {
            const string insertSessionSql = """
                INSERT INTO "ReadingSessions" (
                    "UserId", "MediaId", "SessionDate", "StartPage", "EndPage", "EndPercentage", "StartPercentage",
                    "Status", "RenderedPage", "RenderedTotalPages", "RenderedProfileKey", "CreatedAt", "UpdatedAt"
                ) VALUES (
                    @UserId, @MediaId, @SessionDate, @StartPage, @EndPage, @EndPercentage, @StartPercentage,
                    @Status, @RenderedPage, @RenderedTotalPages, @RenderedProfileKey, @CreatedAt, @UpdatedAt
                );
                """;
            await conn.ExecuteAsync(insertSessionSql, session!);
        }
        else
        {
            const string updateSessionSql = """
                UPDATE "ReadingSessions"
                SET "StartPage" = @StartPage,
                    "EndPage" = @EndPage,
                    "EndPercentage" = @EndPercentage,
                    "StartPercentage" = @StartPercentage,
                    "Status" = @Status,
                    "RenderedPage" = @RenderedPage,
                    "RenderedTotalPages" = @RenderedTotalPages,
                    "RenderedProfileKey" = @RenderedProfileKey,
                    "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id;
                """;
            await conn.ExecuteAsync(updateSessionSql, session!);
        }

        return true;
    }

    private static void ApplyProgressUpdate(
        ReadingSession session,
        bool isEpub,
        int page,
        int total,
        string? profileKey,
        ReadingProgressUpdate update)
    {
        if (isEpub)
        {
            session.RenderedPage = page;
            session.RenderedTotalPages = total;
            session.RenderedProfileKey = profileKey;
        }
        else
        {
            session.StartPage ??= page;
            session.EndPage = page;
            session.EndPercentage = update.Percentage ?? Percentage(page, total);
            session.StartPercentage ??= 0m;
        }

        session.Status = (update.Completed ?? (total > 0 && page >= total))
            ? ReadingStatus.Finished
            : ReadingStatus.Reading;
        session.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public async Task<ReadingProgressView> ResolveAsync(
        Media book,
        ReadingSession? session,
        CancellationToken ct = default)
    {
        if (!EpubPageMapStore.IsEpub(book))
        {
            return new ReadingProgressView(session?.EndPage, Math.Max(1, book.Pages), false);
        }

        var profile = await _profiles.GetCurrentProfileAsync(ct);
        var profileKey = EpubRasterizer.BuildProfileKey(profile);
        var total = await _pageMaps.GetTotalPagesAsync(book, profile, ct);

        if (session?.RenderedPage is not int storedPage)
        {
            return new ReadingProgressView(session?.EndPage, total, false);
        }

        var storedTotal = session.RenderedTotalPages ?? 0;
        var sameProfile = string.Equals(session.RenderedProfileKey, profileKey, StringComparison.Ordinal);

        if (sameProfile || storedTotal <= 0)
        {
            return new ReadingProgressView(Math.Clamp(storedPage, 1, Math.Max(1, total)), total, false);
        }

        var converted = Rescale(storedPage, storedTotal, total);

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string updateSql = """
            UPDATE "ReadingSessions"
            SET "RenderedPage" = @Converted,
                "RenderedTotalPages" = @Total,
                "RenderedProfileKey" = @ProfileKey
            WHERE "Id" = @Id;
            """;

        await conn.ExecuteAsync(updateSql, new { Converted = converted, Total = total, ProfileKey = profileKey, Id = session.Id });

        session.RenderedPage = converted;
        session.RenderedTotalPages = total;
        session.RenderedProfileKey = profileKey;

        _logger.LogInformation(
            "[EPUB] Progreso de {Name} reconvertido de {OldPage}/{OldTotal} a {NewPage}/{NewTotal} al cambiar de maquetacion",
            book.Name, storedPage, storedTotal, converted, total);

        return new ReadingProgressView(converted, total, true);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetStoredTotalsAsync(
        IReadOnlyCollection<string> mediaIds,
        CancellationToken ct = default)
    {
        if (mediaIds.Count == 0) return new Dictionary<string, int>();

        var profileKey = await CurrentProfileKeyAsync(ct);

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string sql = """
            SELECT "MediaId", "TotalPages"
            FROM "EpubPageMaps"
            WHERE "MediaId" IN (SELECT value FROM json_each(@MediaIds)) AND "ProfileKey" = @ProfileKey AND "TotalPages" > 0;
            """;

        var rows = await conn.QueryAsync<MediaTotalPagesRow>(
            sql, new { MediaIds = SqlKeys.ToJson(mediaIds), ProfileKey = profileKey });

        return rows.ToDictionary(r => r.MediaId, r => r.TotalPages, StringComparer.Ordinal);
    }

    private async Task<int> ResolveTotalAsync(Media book, CancellationToken ct)
    {
        if (!EpubPageMapStore.IsEpub(book)) return book.Pages;

        var profile = await _profiles.GetCurrentProfileAsync(ct);
        return await _pageMaps.GetTotalPagesAsync(book, profile, ct);
    }

    private async Task<string> CurrentProfileKeyAsync(CancellationToken ct) =>
        EpubRasterizer.BuildProfileKey(await _profiles.GetCurrentProfileAsync(ct));

    private static decimal Percentage(int page, int total) =>
        total > 0 ? Math.Clamp((decimal)page / total, 0m, 1m) : 0m;

    private static int Rescale(int page, int fromTotal, int toTotal) =>
        Math.Clamp(
            (int)Math.Round((decimal)page / fromTotal * toTotal, MidpointRounding.AwayFromZero),
            1,
            Math.Max(1, toTotal));
}
