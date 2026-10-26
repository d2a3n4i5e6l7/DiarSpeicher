using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Data.Extensions;

namespace DiarSpeicher.Infrastructure.Filesystem;

public partial class LibraryScannerService
{
    private async Task<List<Series>> CreateNewSeriesAsync(
        string libraryId,
        string libraryPath,
        List<string> seriesToCreate,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        var newlyCreated = new List<Series>();
        var now = DateTimeOffset.UtcNow;
        foreach (var newSeriesPath in seriesToCreate)
        {
            var dirName = Path.GetFileName(newSeriesPath);
            if (string.IsNullOrEmpty(dirName))
            {
                dirName = Path.GetFileName(libraryPath);
            }

            var newSeries = new Series
            {
                Id = Ulid.NewUlid().ToString(),
                Name = dirName ?? string.Empty,
                Path = newSeriesPath,
                LibraryId = libraryId,
                Status = FileStatus.Ready,
                CreatedAt = now,
                UpdatedAt = now
            };

            newlyCreated.Add(newSeries);
            report.CreatedSeries++;
        }

        if (newlyCreated.Count > 0)
        {
            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            const string insertSql = """
                INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @Name, @Path, @LibraryId, @Status, @CreatedAt, @UpdatedAt);
                """;

            await conn.ExecuteAsync(insertSql, newlyCreated.Select(s => new { s.Id, s.Name, s.Path, s.LibraryId, Status = s.Status.ToString(), s.CreatedAt, s.UpdatedAt }), tx);
            await tx.CommitAsync(cancellationToken);

            await PublishProgressAsync(libraryId, ScanPhase.WalkingLibrary, cancellationToken, new()
            {
                TotalSeries = seriesToCreate.Count,
                CreatedSeries = newlyCreated.Select(s => CatalogDtoMapper.ToSeriesDto(s)).ToList(),
                Message = $"{newlyCreated.Count} series nuevas en el indice"
            });
        }

        return newlyCreated;
    }

    private async Task<Dictionary<string, long>> ProcessAllSeriesAsync(
        ScanContext context,
        IEnumerable<string> seriesPaths,
        CancellationToken cancellationToken)
    {
        var libraryId = context.LibraryId;
        var distinctPaths = seriesPaths.Distinct(StringComparer.Ordinal).ToList();

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT "Id", "Name", "Description", "Path", "Status", "ThumbnailPath", "ThumbnailMeta", "CreatedAt", "UpdatedAt", "DeletedAt", "LibraryId"
            FROM "Series"
            WHERE "LibraryId" = @LibraryId AND "Path" IN (SELECT value FROM json_each(@Paths));
            """;
        var seriesEntities = (await conn.QueryAsync<Series>(
            sql, new { LibraryId = libraryId, Paths = SqlKeys.ToJson(distinctPaths) }
        )).AsRowList();

        context.AllSeriesPaths = distinctPaths;
        var completed = 0;

        foreach (var series in seriesEntities)
        {
            await PublishProgressAsync(libraryId, ScanPhase.ProcessingSeries, cancellationToken, new() { CompletedSeries = completed, TotalSeries = seriesEntities.Count, CurrentSeries = series.Name });

            await ProcessSingleSeriesAsync(
                series,
                context,
                new SeriesProgress(libraryId, series.Id, series.Name, completed, seriesEntities.Count),
                cancellationToken);
            completed++;
        }

        return context.ObservedDirMtimes;
    }

    private async Task ProcessSingleSeriesAsync(
        Series series,
        ScanContext context,
        SeriesProgress progress,
        CancellationToken cancellationToken)
    {
        var (allSeriesPaths, storedMtimes, thumbnailsDir, settings, report) =
            (context.AllSeriesPaths, context.StoredMtimes, context.ThumbnailsDir, context.Settings, context.Report);
        var allObservedDirMtimes = context.ObservedDirMtimes;

        // Conversion runs before looking at the disk: otherwise the walk would see the CBR that
        // is about to disappear and not yet the CBZ.
        if (settings.ConvertRarToZip)
        {
            var conversions = await _archiveConverter.ConvertDirectoryAsync(
                series.Path,
                settings.HardDeleteConversions,
                cancellationToken);

            await RepointConvertedMediaAsync(series.Id, conversions, report, cancellationToken);
        }

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string mediaSql = @"SELECT ""Id"", ""Path"", ""Status"", ""ModifiedAt"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId;";
        var existingMedia = (await conn.QueryAsync<ExistingMediaInfo>(
            mediaSql, new { SeriesId = series.Id }
        )).AsRowList();

        var walkedSeries = _directoryScanner.WalkSeries(series.Path, storedMtimes, existingMedia, allSeriesPaths);

        report.TotalFiles += walkedSeries.SeenFiles;
        report.IgnoredFiles += walkedSeries.IgnoredFiles;
        report.SkippedFiles += walkedSeries.SkippedFiles;

        foreach (var (dirPath, mtime) in walkedSeries.ObservedDirMtimes)
        {
            allObservedDirMtimes[dirPath] = mtime;
        }

        var mediaToCreate = walkedSeries.MediaToCreate.Where(path => !HasConvertedSibling(path)).ToList();

        if (existingMedia.Count == 0)
        {
            await CreateNewMediaAsync(series.Id, mediaToCreate, thumbnailsDir, settings, report, progress, cancellationToken);
        }
        else
        {
            await MarkMissingMediaAsync(series.Id, walkedSeries.MissingMedia, report, cancellationToken);
            await MarkRecoveredMediaAsync(walkedSeries.RecoveredMedia, report, cancellationToken);
            await CreateNewMediaAsync(series.Id, mediaToCreate, thumbnailsDir, settings, report, progress, cancellationToken);
            await UpdateVisitedMediaAsync(series.Id, walkedSeries.MediaToVisit, thumbnailsDir, settings, report, cancellationToken);
        }
    }

    /// <summary>
    /// Without this the CBR would be marked as gone and the CBZ would enter as a new book, and
    /// the user would lose the reading progress attached to the previous media.
    /// </summary>
    private async Task RepointConvertedMediaAsync(
        string seriesId,
        IReadOnlyList<ArchiveConversion> conversions,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        if (conversions.Count == 0) return;

        var sourcePaths = conversions.Select(c => c.SourcePath).ToList();
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string selectSql = @"SELECT ""Id"", ""Path"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId AND ""Path"" IN (SELECT value FROM json_each(@SourcePaths));";
        var affected = (await conn.QueryAsync<IdPathRow>(
            selectSql, new { SeriesId = seriesId, SourcePaths = SqlKeys.ToJson(sourcePaths) }
        )).AsRowList();

        if (affected.Count == 0) return;

        var bySource = conversions.ToDictionary(c => c.SourcePath, StringComparer.Ordinal);
        var toUpdate = new List<MediaPathUpdate>();
        var now = DateTimeOffset.UtcNow;

        foreach (var item in affected)
        {
            if (!bySource.TryGetValue(item.Path, out var conversion)) continue;

            toUpdate.Add(new MediaPathUpdate
            {
                Id = item.Id,
                Path = conversion.TargetPath,
                Extension = "cbz",
                Status = nameof(FileStatus.Ready),
                UpdatedAt = now
            });
            report.UpdatedMedia++;
        }

        if (toUpdate.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            const string updateSql = """
                UPDATE "Media"
                SET "Path" = @Path, "Extension" = @Extension, "Status" = @Status, "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id;
                """;
            await conn.ExecuteAsync(updateSql, toUpdate, tx);
            await tx.CommitAsync(cancellationToken);
        }
    }

    private async Task MarkMissingMediaAsync(string seriesId, List<string> missingPaths, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (missingPaths.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string updateSql = @"UPDATE ""Media"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""SeriesId"" = @SeriesId AND ""Path"" IN (SELECT value FROM json_each(@Paths)) AND ""Status"" != @Status;";
        var count = await conn.ExecuteAsync(updateSql, new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, SeriesId = seriesId, Paths = SqlKeys.ToJson(missingPaths) });
        report.UpdatedMedia += (ulong)count;
    }

    private async Task MarkRecoveredMediaAsync(List<string> recoveredIds, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (recoveredIds.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string updateSql = @"UPDATE ""Media"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" IN (SELECT value FROM json_each(@Ids)) AND ""Status"" != @Status;";
        var count = await conn.ExecuteAsync(updateSql, new { Status = nameof(FileStatus.Ready), UpdatedAt = DateTimeOffset.UtcNow, Ids = SqlKeys.ToJson(recoveredIds) });
        report.UpdatedMedia += (ulong)count;
    }
}
