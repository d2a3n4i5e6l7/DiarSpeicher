using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Reconciliacion entre lo indexado y lo que queda en disco: que falta y como purgarlo.
/// </summary>
public sealed partial class DiarSpeicherService
{
    private const string LibraryIdParam = "LibraryId";

    private sealed record MissingSeriesRow(string Id, string Name, string Path, long VolumeCount, long ReadingSessions);

    public async Task<DiarSpeicherMissingReportDto> GetMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var (seriesWhere, seriesParams) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        seriesParams.Add(LibraryIdParam, libraryId);
        seriesParams.Add("Status", FileStatus.Missing.ToString());

        var seriesSql = $"""
            SELECT s."Id", s."Name", s."Path",
                   (SELECT COUNT(*) FROM "Media" m WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "VolumeCount",
                   (SELECT COUNT(*) FROM "ReadingSessions" rs INNER JOIN "Media" m ON rs."MediaId" = m."Id" WHERE m."SeriesId" = s."Id" AND m."DeletedAt" IS NULL) AS "ReadingSessions"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" = @LibraryId AND s."Status" = @Status AND {seriesWhere};
            """;

        var seriesRows = (await conn.QueryAsync<MissingSeriesRow>(
            new CommandDefinition(seriesSql, seriesParams, cancellationToken: ct)
        )).AsList();

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

        var (mediaWhere, mediaParams) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        mediaParams.Add(LibraryIdParam, libraryId);
        mediaParams.Add("MissingStatus", FileStatus.Missing.ToString());

        var orphansSql = $"""
            SELECT COUNT(rs."Id") AS "Sessions"
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            LEFT JOIN "ReadingSessions" rs ON rs."MediaId" = m."Id"
            WHERE s."LibraryId" = @LibraryId
              AND m."Status" = @MissingStatus
              AND s."Status" != @MissingStatus
              AND {mediaWhere}
            GROUP BY m."Id";
            """;

        var orphanSessions = (await conn.QueryAsync<int>(
            new CommandDefinition(orphansSql, mediaParams, cancellationToken: ct)
        )).AsList();

        report.OrphanVolumes = orphanSessions.Count;
        report.TotalVolumes = report.Series.Sum(s => s.VolumeCount) + orphanSessions.Count;
        report.TotalReadingSessions = report.Series.Sum(s => s.ReadingSessions) + orphanSessions.Sum();

        return report;
    }

    /// <summary>
    /// Quita del indice lo que ya no esta en disco. Nunca toca un fichero.
    /// </summary>
    public async Task<int> PurgeMissingAsync(AuthUser user, string libraryId, CancellationToken ct = default)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);

        var (seriesWhere, seriesParams) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        seriesParams.Add(LibraryIdParam, libraryId);
        seriesParams.Add("Status", FileStatus.Missing.ToString());

        var seriesSql = $"""
            SELECT s."Id", s."Path", s."ThumbnailPath"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" = @LibraryId AND s."Status" = @Status AND {seriesWhere};
            """;

        var seriesList = (await conn.QueryAsync<(string Id, string Path, string? ThumbnailPath)>(
            new CommandDefinition(seriesSql, seriesParams, cancellationToken: ct)
        )).AsList();

        var goneSeries = seriesList.Where(s => !Directory.Exists(s.Path)).ToList();

        var (mediaWhere, mediaParams) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        mediaParams.Add(LibraryIdParam, libraryId);
        mediaParams.Add("MissingStatus", FileStatus.Missing.ToString());

        var mediaSql = $"""
            SELECT m."Id", m."Path", m."ThumbnailPath"
            FROM "Media" m
            INNER JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE s."LibraryId" = @LibraryId AND m."Status" = @MissingStatus AND {mediaWhere};
            """;

        var mediaList = (await conn.QueryAsync<(string Id, string Path, string? ThumbnailPath)>(
            new CommandDefinition(mediaSql, mediaParams, cancellationToken: ct)
        )).AsList();

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
                const string deleteMediaSql = @"DELETE FROM ""Media"" WHERE ""Id"" IN @Ids;";
                await conn.ExecuteAsync(new CommandDefinition(deleteMediaSql, new { Ids = goneMedia.Select(m => m.Id) }, tx, cancellationToken: ct));
            }
            if (goneSeries.Count > 0)
            {
                const string deleteSeriesSql = @"DELETE FROM ""Series"" WHERE ""Id"" IN @Ids;";
                await conn.ExecuteAsync(new CommandDefinition(deleteSeriesSql, new { Ids = goneSeries.Select(s => s.Id) }, tx, cancellationToken: ct));
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

        var (mediaWhere, mediaParams) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        mediaParams.Add("Root", root);
        mediaParams.Add("Prefix", prefix);
        mediaParams.Add("PrefixLen", prefix.Length);

        var mediaSql = $"""
            SELECT m."Id", m."Path", m."ThumbnailPath"
            FROM "Media" m
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE (m."Path" = @Root OR substr(m."Path", 1, @PrefixLen) = @Prefix) AND {mediaWhere};
            """;

        var media = (await conn.QueryAsync<(string Id, string Path, string? ThumbnailPath)>(
            new CommandDefinition(mediaSql, mediaParams, cancellationToken: ct)
        )).AsList();

        var (seriesWhere, seriesParams) = CatalogSqlFilters.BuildSeriesVisibilityFilter(user, "s", "sm");
        seriesParams.Add("Root", root);
        seriesParams.Add("Prefix", prefix);
        seriesParams.Add("PrefixLen", prefix.Length);

        var seriesSql = $"""
            SELECT s."Id", s."Path", s."ThumbnailPath"
            FROM "Series" s
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE (s."Path" = @Root OR substr(s."Path", 1, @PrefixLen) = @Prefix) AND {seriesWhere};
            """;

        var series = (await conn.QueryAsync<(string Id, string Path, string? ThumbnailPath)>(
            new CommandDefinition(seriesSql, seriesParams, cancellationToken: ct)
        )).AsList();

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
                const string deleteMediaSql = @"DELETE FROM ""Media"" WHERE ""Id"" IN @Ids;";
                await conn.ExecuteAsync(new CommandDefinition(deleteMediaSql, new { Ids = media.Select(m => m.Id) }, tx, cancellationToken: ct));
            }
            if (series.Count > 0)
            {
                const string deleteSeriesSql = @"DELETE FROM ""Series"" WHERE ""Id"" IN @Ids;";
                await conn.ExecuteAsync(new CommandDefinition(deleteSeriesSql, new { Ids = series.Select(s => s.Id) }, tx, cancellationToken: ct));
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
