using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService
{
    internal sealed record MissingSeriesRow(string Id, string Name, string Path, long VolumeCount, long ReadingSessions);

    public async Task<DiarSpeicherMissingReportDto> GetMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var seriesParams = CatalogSqlFilters.ArgsFor(user);
        seriesParams.LibraryId = libraryId;
        seriesParams.Status = FileStatus.Missing.ToString();

        const string seriesSql = $"""
            SELECT s."Id", s."Name", s."Path",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "VolumeCount",
                   (SELECT COUNT(*) FROM "ReadingSessions" rs INNER JOIN "Media" m ON rs."MediaId" = m."Id" WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "ReadingSessions"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" = @LibraryId AND s."Status" = @Status AND {CatalogSqlFilters.SeriesVisible};
            """;

        var seriesRows = (await conn.QueryAsync<MissingSeriesRow>(
            seriesSql, seriesParams
        )).AsRowList();

        var report = new DiarSpeicherMissingReportDto
        {
            Series = seriesRows.Select(s => new DiarSpeicherMissingSeriesDto
            {
                Id = s.Id,
                Name = s.Name,
                Path = s.Path,
                VolumeCount = (int)s.VolumeCount,
                ReadingSessions = (int)s.ReadingSessions
            }).ToList()
        };

        var mediaParams = CatalogSqlFilters.ArgsFor(user);
        mediaParams.LibraryId = libraryId;
        mediaParams.MissingStatus = FileStatus.Missing.ToString();

        const string orphansSql = $"""
            SELECT COUNT(rs."Id") AS "Sessions"
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            LEFT JOIN "ReadingSessions" rs ON rs."MediaId" = m."Id"
            WHERE s."LibraryId" = @LibraryId
              AND m."Status" = @MissingStatus
              AND s."Status" != @MissingStatus
              AND {CatalogSqlFilters.MediaVisible}
            GROUP BY m."Id";
            """;

        var orphanSessions = (await conn.QueryAsync<int>(
            orphansSql, mediaParams
        )).AsRowList();

        report.OrphanVolumes = orphanSessions.Count;
        report.TotalVolumes = report.Series.Sum(s => s.VolumeCount) + orphanSessions.Count;
        report.TotalReadingSessions = report.Series.Sum(s => s.ReadingSessions) + orphanSessions.Sum();

        return report;
    }

    /// <summary>Never touches a file: it only removes index rows whose file is gone.</summary>
    public async Task<int> PurgeMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var seriesParams = CatalogSqlFilters.ArgsFor(user);
        seriesParams.LibraryId = libraryId;
        seriesParams.Status = FileStatus.Missing.ToString();

        const string seriesSql = $"""
            SELECT s."Id", s."Path", s."ThumbnailPath"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" = @LibraryId AND s."Status" = @Status AND {CatalogSqlFilters.SeriesVisible};
            """;

        var seriesList = (await conn.QueryAsync<IdPathThumbnailRow>(
            seriesSql, seriesParams
        )).AsRowList();

        var goneSeries = seriesList.Where(s => !Directory.Exists(s.Path)).ToList();

        var mediaParams = CatalogSqlFilters.ArgsFor(user);
        mediaParams.LibraryId = libraryId;
        mediaParams.MissingStatus = FileStatus.Missing.ToString();

        const string mediaSql = $"""
            SELECT m."Id", m."Path", m."ThumbnailPath"
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" = @LibraryId AND m."Status" = @MissingStatus AND {CatalogSqlFilters.MediaVisible};
            """;

        var mediaList = (await conn.QueryAsync<IdPathThumbnailRow>(
            mediaSql, mediaParams
        )).AsRowList();

        var goneMedia = mediaList.Where(m => !File.Exists(m.Path)).ToList();

        foreach (var item in goneMedia.Where(item => !string.IsNullOrWhiteSpace(item.ThumbnailPath)))
        {
            TryDeleteFile(item.ThumbnailPath);
        }

        foreach (var s in goneSeries.Where(s => !string.IsNullOrWhiteSpace(s.ThumbnailPath)))
        {
            TryDeleteFile(s.ThumbnailPath);
        }

        if (goneMedia.Count > 0 || goneSeries.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (goneMedia.Count > 0)
            {
                const string deleteMediaSql = @"DELETE FROM ""Media"" WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
                await conn.ExecuteAsync(deleteMediaSql, new { Ids = SqlKeys.ToJson(goneMedia.Select(m => m.Id)) }, tx);
            }
            if (goneSeries.Count > 0)
            {
                const string deleteSeriesSql = @"DELETE FROM ""Series"" WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
                await conn.ExecuteAsync(deleteSeriesSql, new { Ids = SqlKeys.ToJson(goneSeries.Select(s => s.Id)) }, tx);
            }
            await tx.CommitAsync(ct);
        }

        var removed = goneSeries.Count + goneMedia.Count;
        _logger.LogInformation(
            "Purgadas {Count} entradas perdidas de la biblioteca {Library}", removed, libraryId);

        return removed;
    }

    public async Task<int> PurgeIndexUnderPathAsync(AuthUser user, string path, CancellationToken ct = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix = root + Path.DirectorySeparatorChar;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var mediaParams = CatalogSqlFilters.ArgsFor(user);
        mediaParams.Root = root;
        mediaParams.Prefix = prefix;
        mediaParams.PrefixLen = prefix.Length;

        const string mediaSql = $"""
            SELECT m."Id", m."Path", m."ThumbnailPath"
            FROM "Media" m
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE (m."Path" = @Root OR substr(m."Path", 1, @PrefixLen) = @Prefix) AND {CatalogSqlFilters.MediaVisible};
            """;

        var media = (await conn.QueryAsync<IdPathThumbnailRow>(
            mediaSql, mediaParams
        )).AsRowList();

        var seriesParams = CatalogSqlFilters.ArgsFor(user);
        seriesParams.Root = root;
        seriesParams.Prefix = prefix;
        seriesParams.PrefixLen = prefix.Length;

        const string seriesSql = $"""
            SELECT s."Id", s."Path", s."ThumbnailPath"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE (s."Path" = @Root OR substr(s."Path", 1, @PrefixLen) = @Prefix) AND {CatalogSqlFilters.SeriesVisible};
            """;

        var series = (await conn.QueryAsync<IdPathThumbnailRow>(
            seriesSql, seriesParams
        )).AsRowList();

        var thumbs = media.Select(m => m.ThumbnailPath)
            .Concat(series.Select(s => s.ThumbnailPath))
            .Where(thumb => !string.IsNullOrWhiteSpace(thumb));

        foreach (var thumb in thumbs)
        {
            TryDeleteFile(thumb);
        }

        if (media.Count > 0 || series.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            if (media.Count > 0)
            {
                const string deleteMediaSql = @"DELETE FROM ""Media"" WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
                await conn.ExecuteAsync(deleteMediaSql, new { Ids = SqlKeys.ToJson(media.Select(m => m.Id)) }, tx);
            }
            if (series.Count > 0)
            {
                const string deleteSeriesSql = @"DELETE FROM ""Series"" WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
                await conn.ExecuteAsync(deleteSeriesSql, new { Ids = SqlKeys.ToJson(series.Select(s => s.Id)) }, tx);
            }
            await tx.CommitAsync(ct);
        }

        var removed = media.Count + series.Count;
        if (removed > 0)
        {
            _logger.LogInformation("Sacadas {Count} entradas del indice bajo {Path}", removed, root);
        }

        return removed;
    }
}
