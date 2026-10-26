using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Reading;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    public Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> GetMediaAsync(
        AuthUser user,
        int page,
        int pageSize,
        bool newestFirst = false,
        CancellationToken ct = default)
    {
        return PageMediaAsync(user, seriesId: null, newestFirst: newestFirst, page: page, pageSize: pageSize, ct: ct);
    }

    private async Task<DiarSpeicherPageResponse<DiarSpeicherMediaDto>> PageMediaAsync(
        AuthUser user,
        string? seriesId,
        bool newestFirst,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(0, page);

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);

        if (!string.IsNullOrWhiteSpace(seriesId))
        {
            parameters.SeriesId = seriesId;
        }

        const string countSql = $"""
            SELECT COUNT(*)
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND {CatalogSqlFilters.InSeries};
            """;

        var total = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        parameters.NewestFirst = newestFirst ? 1 : 0;
        parameters.PageSize = pageSize;
        parameters.Offset = page * pageSize;

        const string selectSql = $"""
            SELECT m."Id", m."Name", m."SortName", m."Size", m."Extension", m."Pages", m."Path", m."Status", m."Hash",
                   m."KoreaderHash", m."ThumbnailPath", m."ThumbnailMeta", m."CreatedAt", m."UpdatedAt", m."ModifiedAt",
                   m."DeletedAt", m."SeriesId"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {CatalogSqlFilters.MediaVisible} AND {CatalogSqlFilters.InSeries}
            ORDER BY CASE WHEN @NewestFirst = 1 THEN m."Id" END DESC, m."SortName" ASC, m."Name" ASC
            LIMIT @PageSize OFFSET @Offset;
            """;

        var rows = await conn.QueryAsync<Media>(selectSql, parameters).WithMetadataAsync(conn);

        var mediaList = rows.AsRowList();
        var mediaIds = mediaList.Select(m => m.Id).ToList();
        var sessionMap = await conn.GetLatestSessionsPerMediaAsync(user.Id, mediaIds, ct);

        return new DiarSpeicherPageResponse<DiarSpeicherMediaDto>
        {
            Data = mediaList.Select(m => ToMediaDto(m, sessionMap.GetValueOrDefault(m.Id))).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<DiarSpeicherMediaDto?> GetMediaByIdAsync(AuthUser user, string id, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = id;

        const string sql = $"""
            SELECT m."Id", m."Name", m."SortName", m."Size", m."Extension", m."Pages", m."Path", m."Status", m."Hash",
                   m."KoreaderHash", m."ThumbnailPath", m."ThumbnailMeta", m."CreatedAt", m."UpdatedAt", m."ModifiedAt",
                   m."DeletedAt", m."SeriesId"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn);

        var media = rows.FirstOrDefault();
        if (media == null) return null;

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
            sessionSql, new { UserId = user.Id, MediaId = id });

        return ToMediaDto(media, session);
    }

    public async Task<List<DiarSpeicherMediaDto>> GetKeepReadingAsync(AuthUser user, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var sessions = await conn.GetKeepReadingSessionsAsync(user.Id, ct);
        var mediaIds = sessions.Select(s => s.MediaId).Distinct().ToList();
        if (mediaIds.Count == 0) return [];

        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.MediaIds = SqlKeys.ToJson(mediaIds);

        const string sql = $"""
            SELECT m."Id", m."Name", m."SortName", m."Size", m."Extension", m."Pages", m."Path", m."Status", m."Hash",
                   m."KoreaderHash", m."ThumbnailPath", m."ThumbnailMeta", m."CreatedAt", m."UpdatedAt", m."ModifiedAt",
                   m."DeletedAt", m."SeriesId"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" IN (SELECT value FROM json_each(@MediaIds)) AND {CatalogSqlFilters.MediaVisible};
            """;

        var rows = await conn.QueryAsync<Media>(sql, parameters).WithMetadataAsync(conn);

        var mediaMap = rows.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var sessionMap = sessions.ToDictionary(s => s.MediaId, StringComparer.Ordinal);

        var result = new List<DiarSpeicherMediaDto>();
        foreach (var id in mediaIds)
        {
            if (mediaMap.TryGetValue(id, out var media))
            {
                result.Add(ToMediaDto(media, sessionMap.GetValueOrDefault(id)));
            }
        }

        return result;
    }

    private async Task<Media?> FetchMediaForUserAsync(AuthUser user, string mediaId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var parameters = CatalogSqlFilters.ArgsFor(user);
        parameters.Id = mediaId;

        const string sql = $"""
            SELECT m."Id", m."Name", m."Path", m."Extension", m."ThumbnailPath"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE m."Id" = @Id AND {CatalogSqlFilters.MediaVisible}
            LIMIT 1;
            """;

        return await conn.QueryFirstOrDefaultAsync<Media>(
            sql, parameters);
    }

    public async Task<OpenedPage?> OpenMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default)
    {
        var media = await FetchMediaForUserAsync(user, mediaId, ct);
        if (media == null || !File.Exists(media.Path)) return null;

        var opened = await _bookProcessor.OpenPageAsync(media.Path, page, ct);
        if (opened == null) return null;

        try
        {
            await _progress.RecordAsync(user, media, new ReadingProgressUpdate(page), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update reading session for user {UserId}, media {MediaId}", user.Id, mediaId);
        }

        return opened;
    }

    public async Task<ExtractedPage?> GetMediaPageAsync(AuthUser user, string mediaId, int page, CancellationToken ct = default)
    {
        var media = await FetchMediaForUserAsync(user, mediaId, ct);
        if (media == null || !File.Exists(media.Path)) return null;

        var extracted = await _bookProcessor.ExtractPageAsync(media.Path, page, ct);
        if (extracted != null)
        {
            try
            {
                await _progress.RecordAsync(user, media, new ReadingProgressUpdate(page), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update reading session for user {UserId}, media {MediaId}", user.Id, mediaId);
            }
        }

        return extracted;
    }

    public async Task<(string Path, string ContentType)?> GetMediaFileAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        var media = await FetchMediaForUserAsync(user, mediaId, ct);
        if (media == null || !File.Exists(media.Path)) return null;

        return (media.Path, ContentTypeExtensions.FromExtension(media.Extension).ToMimeType());
    }

    public async Task<bool> UpdateProgressAsync(AuthUser user, string mediaId, DiarSpeicherUpdateProgressInput input, CancellationToken ct = default)
    {
        var media = await FetchMediaForUserAsync(user, mediaId, ct);
        if (media == null) return false;

        return await _progress.RecordAsync(
            user,
            media,
            new ReadingProgressUpdate(input.Page, input.IsCompleted, (decimal?)input.Percentage),
            ct);
    }

    public async Task<bool> DeleteMediaAsync(AuthUser user, string id, bool deleteFile = false, CancellationToken ct = default)
    {
        var media = await FetchMediaForUserAsync(user, id, ct);
        if (media == null) return false;

        if (deleteFile)
        {
            EnsureTrashed(media.Path, "el fichero");
            await PurgeIndexUnderPathAsync(user, media.Path, ct);

            var checkStillExists = await FetchMediaForUserAsync(user, id, ct);
            if (checkStillExists == null) return true;
        }

        if (!string.IsNullOrWhiteSpace(media.ThumbnailPath))
        {
            TryDeleteFile(media.ThumbnailPath);
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
        const string deleteSql = @"DELETE FROM ""Media"" WHERE ""Id"" = @Id;";
        await conn.ExecuteAsync(deleteSql, new { Id = id });

        return true;
    }

    private void EnsureTrashed(string path, string what)
    {
        switch (_trash.TryMoveToTrash(path, out _))
        {
            case TrashOutcome.Moved:
            case TrashOutcome.NotFound:
                return;
            case TrashOutcome.NotAllowed:
                throw new InvalidOperationException(
                    $"No se puede borrar {what} del disco: esa ruta esta fuera de las carpetas declaradas en el compose. El indice no se ha tocado.");
            default:
                throw new InvalidOperationException(
                    $"No se pudo mover {what} a la papelera. El indice no se ha tocado.");
        }
    }
}
