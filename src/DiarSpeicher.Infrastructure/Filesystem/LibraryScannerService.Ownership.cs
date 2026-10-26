using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using DiarSpeicher.Infrastructure.Data.Extensions;

namespace DiarSpeicher.Infrastructure.Filesystem;

public partial class LibraryScannerService
{
    private static List<(string MediaId, string NewSeriesId)> FindMediaToRepoint(
        Dictionary<string, string> owners,
        List<MediaOwnershipItem> mediaList)
    {
        var toRepoint = new List<(string MediaId, string NewSeriesId)>();
        foreach (var item in mediaList)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(item.Path));
            if (string.IsNullOrEmpty(dir)) continue;

            var owner = FindOwningSeries(owners, dir);
            if (owner is null || string.Equals(owner, item.SeriesId, StringComparison.Ordinal)) continue;

            toRepoint.Add((item.Id, owner));
        }

        return toRepoint;
    }

    private async Task ReconcileMediaOwnershipAsync(
        string libraryId,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string seriesSql = @"SELECT ""Id"", ""Path"" FROM ""Series"" WHERE ""LibraryId"" = @LibraryId;";
        var seriesOfLibrary = (await conn.QueryAsync<IdPathRow>(
            seriesSql, new { LibraryId = libraryId }
        )).AsRowList();

        if (seriesOfLibrary.Count == 0) return;

        // Deepest first: the owner of a file is the nearest series above it.
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in seriesOfLibrary)
        {
            owners.TryAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(s.Path)), s.Id);
        }

        var seriesIds = seriesOfLibrary.Select(s => s.Id).ToList();
        const string mediaSql = @"SELECT ""Id"", ""Path"", ""SeriesId"", ""CreatedAt"", ""ThumbnailPath"" FROM ""Media"" WHERE ""SeriesId"" IN (SELECT value FROM json_each(@SeriesIds));";
        var mediaList = (await conn.QueryAsync<MediaOwnershipItem>(
            mediaSql, new { SeriesIds = SqlKeys.ToJson(seriesIds) }
        )).AsRowList();

        if (mediaList.Count == 0) return;

        var toRepoint = FindMediaToRepoint(owners, mediaList);

        var removed = await RemoveDuplicateMediaRowsAsync(conn, mediaList, cancellationToken);

        if (toRepoint.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            const string updateSql = @"UPDATE ""Media"" SET ""SeriesId"" = @NewSeriesId, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" = @MediaId;";
            var now = DateTimeOffset.UtcNow;
            await conn.ExecuteAsync(
                updateSql,
                toRepoint.Select(x => new { NewSeriesId = x.NewSeriesId, UpdatedAt = now, MediaId = x.MediaId }),
                tx);
            await tx.CommitAsync(cancellationToken);
            report.UpdatedMedia += (ulong)toRepoint.Count;
        }

        if (toRepoint.Count > 0 || removed > 0)
        {
            _logger.LogInformation(
                "Reconciled media for library {LibraryId}: {Repointed} repointed, {Removed} duplicate rows removed.",
                libraryId,
                toRepoint.Count,
                removed);
        }
    }

    private async Task<int> RemoveDuplicateMediaRowsAsync(
        SqliteConnection conn,
        IReadOnlyList<MediaOwnershipItem> media,
        CancellationToken cancellationToken)
    {
        var duplicates = media
            .GroupBy(m => Path.GetFullPath(m.Path), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count == 0) return 0;

        var duplicateIds = duplicates.SelectMany(g => g.Select(m => m.Id)).ToList();
        const string sessionsSql = @"SELECT ""MediaId"", COUNT(*) AS ""Count"" FROM ""ReadingSessions"" WHERE ""MediaId"" IN (SELECT value FROM json_each(@DuplicateIds)) GROUP BY ""MediaId"";";
        var sessionCounts = (await conn.QueryAsync<MediaCountRow>(
            sessionsSql, new { DuplicateIds = SqlKeys.ToJson(duplicateIds) }
        )).ToDictionary(x => x.MediaId, x => x.Count, StringComparer.Ordinal);

        var removed = 0;
        var toDeleteIds = new List<string>();
        foreach (var group in duplicates)
        {
            var survivor = group
                .OrderByDescending(m => sessionCounts.TryGetValue(m.Id, out var count) ? count : 0)
                .ThenBy(m => m.CreatedAt)
                .First();

            foreach (var loser in group.Where(m => !ReferenceEquals(m, survivor)))
            {
                // The thumbnail carries the volume id in its name, so the discarded one is not
                // the survivor and can be deleted without leaving it coverless.
                TryDeleteThumbnail(loser.ThumbnailPath);
                toDeleteIds.Add(loser.Id);
                removed++;
            }
        }

        if (toDeleteIds.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            const string deleteSql = @"DELETE FROM ""Media"" WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
            await conn.ExecuteAsync(deleteSql, new { Ids = SqlKeys.ToJson(toDeleteIds) }, tx);
            await tx.CommitAsync(cancellationToken);
        }

        return removed;
    }

    private void TryDeleteThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "No se pudo borrar la miniatura {Path}", path);
        }
    }

    private static string? FindOwningSeries(Dictionary<string, string> owners, string directory)
    {
        var actual = Path.TrimEndingDirectorySeparator(directory);

        while (!string.IsNullOrEmpty(actual))
        {
            if (owners.TryGetValue(actual, out var id)) return id;

            var padre = Path.GetDirectoryName(actual);
            if (padre is null || string.Equals(padre, actual, StringComparison.Ordinal)) return null;

            actual = padre;
        }

        return null;
    }

    private async Task ProcessMissingSeriesAsync(string libraryId, List<string> missingPaths, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (missingPaths.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        const string selectSeriesSql = @"SELECT ""Id"" FROM ""Series"" WHERE ""LibraryId"" = @LibraryId AND ""Path"" IN (SELECT value FROM json_each(@Paths));";
        var missingSeriesIds = (await conn.QueryAsync<string>(
            selectSeriesSql, new { LibraryId = libraryId, Paths = SqlKeys.ToJson(missingPaths) }, tx
        )).AsRowList();

        if (missingSeriesIds.Count > 0)
        {
            const string updateSeriesSql = @"UPDATE ""Series"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
            var updatedSeriesCount = await conn.ExecuteAsync(updateSeriesSql, new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, Ids = SqlKeys.ToJson(missingSeriesIds) }, tx);
            report.UpdatedSeries += (ulong)updatedSeriesCount;

            const string updateMediaSql = @"UPDATE ""Media"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""SeriesId"" IN (SELECT value FROM json_each(@SeriesIds)) AND ""Status"" != @Status;";
            var updatedMediaCount = await conn.ExecuteAsync(updateMediaSql, new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, SeriesIds = SqlKeys.ToJson(missingSeriesIds) }, tx);
            report.UpdatedMedia += (ulong)updatedMediaCount;
        }

        await tx.CommitAsync(cancellationToken);
    }

    private async Task ProcessRecoveredSeriesAsync(List<string> recoveredIds, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (recoveredIds.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string updateSql = @"UPDATE ""Series"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" IN (SELECT value FROM json_each(@Ids));";
        var updatedCount = await conn.ExecuteAsync(updateSql, new { Status = nameof(FileStatus.Ready), UpdatedAt = DateTimeOffset.UtcNow, Ids = SqlKeys.ToJson(recoveredIds) });
        report.UpdatedSeries += (ulong)updatedCount;
    }
}
