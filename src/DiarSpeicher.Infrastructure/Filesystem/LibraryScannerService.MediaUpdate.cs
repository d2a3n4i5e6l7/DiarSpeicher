using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using Microsoft.Extensions.Logging;
using DiarSpeicher.Infrastructure.Data.Extensions;

namespace DiarSpeicher.Infrastructure.Filesystem;

public partial class LibraryScannerService
{
    private async Task UpdateVisitedMediaAsync(
        string seriesId,
        List<string> toVisitPaths,
        string thumbnailsDir,
        ScanSettings settings,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        if (toVisitPaths.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string selectSql = @"SELECT ""Id"", ""Path"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId AND ""Path"" IN (SELECT value FROM json_each(@Paths));";
        var mediaToUpdate = (await conn.QueryAsync<IdPathRow>(
            selectSql, new { SeriesId = seriesId, Paths = SqlKeys.ToJson(toVisitPaths) }
        )).AsRowList();

        var existingOnDisk = mediaToUpdate.Where(m => File.Exists(m.Path)).ToList();
        if (existingOnDisk.Count == 0) return;

        var prepared = await PrepareMediaAsync(
            existingOnDisk.Select(m => (m.Id, m.Path)).ToList(),
            thumbnailsDir,
            settings.Analysis,
            cancellationToken);

        var byId = prepared.ToDictionary(p => p.MediaId, StringComparer.Ordinal);
        var visitedIds = existingOnDisk.Select(m => m.Id).ToList();

        var mediaUpdateList = new List<MediaRescanUpdate>();
        var newMediaPages = new List<MediaPage>();
        var now = DateTimeOffset.UtcNow;

        foreach (var m in existingOnDisk)
        {
            if (!byId.TryGetValue(m.Id, out var result)) continue;

            var fileInfo = new FileInfo(m.Path);
            if (!fileInfo.Exists) continue;

            var analyzed = result.Analysis;
            var thumbPath = result.ThumbnailPath;

            mediaUpdateList.Add(new MediaRescanUpdate
            {
                Id = m.Id,
                Size = fileInfo.Length,
                Pages = analyzed.Pages,
                Hash = analyzed.Hash,
                KoreaderHash = analyzed.KoreaderHash,
                ThumbnailPath = thumbPath,
                ModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc),
                Status = nameof(FileStatus.Ready),
                UpdatedAt = now
            });

            foreach (var page in analyzed.PageDimensions)
            {
                newMediaPages.Add(new MediaPage
                {
                    MediaId = m.Id,
                    Number = page.Number,
                    FileName = page.FileName,
                    MediaType = page.MediaType,
                    Width = page.Width,
                    Height = page.Height,
                    SizeBytes = page.SizeBytes
                });
            }

            report.UpdatedMedia++;
        }

        if (mediaUpdateList.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);

            const string updateMediaSql = """
                UPDATE "Media"
                SET "Size" = @Size,
                    "Pages" = @Pages,
                    "Hash" = @Hash,
                    "KoreaderHash" = @KoreaderHash,
                    "ThumbnailPath" = COALESCE(@ThumbnailPath, "ThumbnailPath"),
                    "ModifiedAt" = @ModifiedAt,
                    "Status" = @Status,
                    "UpdatedAt" = @UpdatedAt
                WHERE "Id" = @Id;
                """;
            await conn.ExecuteAsync(updateMediaSql, mediaUpdateList, tx);

            const string deletePagesSql = @"DELETE FROM ""MediaPages"" WHERE ""MediaId"" IN (SELECT value FROM json_each(@MediaIds));";
            await conn.ExecuteAsync(deletePagesSql, new { MediaIds = SqlKeys.ToJson(visitedIds) }, tx);

            if (newMediaPages.Count > 0)
            {
                const string insertPagesSql = """
                    INSERT INTO "MediaPages" ("MediaId", "Number", "FileName", "MediaType", "Width", "Height", "SizeBytes")
                    VALUES (@MediaId, @Number, @FileName, @MediaType, @Width, @Height, @SizeBytes);
                    """;
                await conn.ExecuteAsync(insertPagesSql, newMediaPages, tx);
            }

            await tx.CommitAsync(cancellationToken);
        }
    }

    private static MediaMetadata MapMediaMetadata(ExtractedMetadata extracted)
    {
        return new MediaMetadata
        {
            AgeRating = extracted.AgeRating,
            Summary = extracted.Summary,
            Publisher = extracted.Publisher,
            Genres = extracted.Genre,
            Number = (decimal?)extracted.Number,
            Volume = extracted.Volume,
            Year = extracted.Year,
            Month = extracted.Month,
            Day = extracted.Day,
            Writers = extracted.Writers,
            Pencillers = extracted.Pencillers,
            Inkers = extracted.Inkers,
            Colorists = extracted.Colorists,
            Letterers = extracted.Letterers,
            CoverArtists = extracted.CoverArtists,
            Editors = extracted.Editors
        };
    }

    /// <summary>
    /// Compared byte by byte, like the table primary key and the file system itself: on Linux
    /// "parte 2" and "Parte 2" are different folders and the table has a row for each, so a
    /// case-insensitive dictionary would abort the scan with ArgumentException.
    ///
    /// Rows whose folder is gone from disk are dropped along the way; otherwise every renamed or
    /// deleted folder would leave its row in the table forever.
    /// </summary>
    private async Task<Dictionary<string, long>> LoadDirectoryMtimeCacheAsync(
        string libraryPath,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string selectSql = @"SELECT ""Path"", ""LastMTime"" FROM ""ScannedDirectories"" WHERE substr(""Path"", 1, @Len) = @Prefix;";
        var stored = (await conn.QueryAsync<ScannedDirectory>(
            selectSql, new { Prefix = libraryPath, Len = libraryPath.Length }
        )).AsRowList();

        var cache = new Dictionary<string, long>(StringComparer.Ordinal);
        var stalePaths = new List<string>();

        foreach (var entry in stored)
        {
            if (Directory.Exists(entry.Path))
            {
                cache[entry.Path] = entry.LastMTime;
            }
            else
            {
                stalePaths.Add(entry.Path);
            }
        }

        if (stalePaths.Count > 0)
        {
            const string deleteSql = @"DELETE FROM ""ScannedDirectories"" WHERE ""Path"" IN (SELECT value FROM json_each(@Paths));";
            await conn.ExecuteAsync(deleteSql, new { Paths = SqlKeys.ToJson(stalePaths) });

            _logger.LogInformation(
                "Dropped {Count} cached directory entries with no folder on disk under {Path}.",
                stalePaths.Count,
                libraryPath);
        }

        return cache;
    }

    private async Task UpsertScannedDirMtimesAsync(Dictionary<string, long> allObservedDirMtimes, CancellationToken cancellationToken)
    {
        if (allObservedDirMtimes.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        const string upsertSql = """
            INSERT INTO "ScannedDirectories" ("Path", "LastMTime")
            VALUES (@Path, @LastMTime)
            ON CONFLICT("Path") DO UPDATE SET "LastMTime" = excluded."LastMTime";
            """;

        var parameters = allObservedDirMtimes.Select(kv => new { Path = kv.Key, LastMTime = kv.Value });
        await conn.ExecuteAsync(upsertSql, parameters, tx);

        await tx.CommitAsync(cancellationToken);
    }
}
