using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Filesystem;

public partial class LibraryScannerService
{
    /// <summary>
    /// Parallel phase of a scan: opens, decompresses and hashes each book and writes its
    /// thumbnail. Deliberately touches no database state, so it is safe to fan out.
    /// A book that fails to process is logged and dropped rather than aborting the batch.
    /// </summary>
    private async Task<List<PreparedMedia>> PrepareMediaAsync(
        List<(string MediaId, string Path)> targets,
        string thumbnailsDir,
        BookAnalysisOptions analysisOptions,
        CancellationToken cancellationToken)
    {
        var results = new PreparedMedia?[targets.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = AnalysisParallelism,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var (mediaId, mediaPath) = targets[index];
                try
                {
                    // includeCover reuses the archive that the analysis already opened,
                    // instead of decompressing the whole book a second time for the cover.
                    var analyzed = await _bookProcessor.AnalyzeAsync(mediaPath, analysisOptions with { IncludeCover = true, MeasurePages = true }, token);
                    var thumbPath = await _thumbnailService.SaveThumbnailAsync(mediaId, analyzed.Cover, thumbnailsDir, token);
                    results[index] = new PreparedMedia(mediaId, mediaPath, analyzed, thumbPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to process media file {Path}; skipping it", mediaPath);
                }
            });

        return results.OfType<PreparedMedia>().ToList();
    }

    private async Task CreateNewMediaAsync(
        string seriesId,
        List<string> mediaToCreate,
        string thumbnailsDir,
        ScanSettings settings,
        LibraryScanReport report,
        SeriesProgress progress,
        CancellationToken cancellationToken)
    {
        if (mediaToCreate.Count == 0) return;

        // The total is announced before touching the disk: the client can draw the slots of the
        // upcoming volumes as soon as it starts, not once they are done.
        var pendingNames = mediaToCreate.ToList();

        await PublishProgressAsync(progress.LibraryId, ScanPhase.ProcessingMedia, cancellationToken, new() { CompletedSeries = progress.CompletedSeries, TotalSeries = progress.TotalSeries, CurrentSeries = progress.SeriesName, TotalMedia = mediaToCreate.Count, PendingMedia = pendingNames, CurrentSeriesId = progress.SeriesId });

        var done = 0;
        var announced = new List<DiarSpeicherMediaDto>();

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        foreach (var chunk in mediaToCreate.Chunk(MediaBatchSize))
        {
            // Announced before the analysis, not after: the analysis is what takes time, and
            // without this the slot of a volume lights up right when it is no longer being
            // read.
            await PublishProgressAsync(progress.LibraryId, ScanPhase.ProcessingMedia, cancellationToken, new() { CompletedSeries = progress.CompletedSeries, TotalSeries = progress.TotalSeries, CurrentSeries = progress.SeriesName, CompletedMedia = done, TotalMedia = mediaToCreate.Count, PendingMedia = pendingNames, ReadingFiles = chunk.ToList(), CreatedMedia = announced.ToList(), CurrentSeriesId = progress.SeriesId });

            var prepared = await PrepareMediaAsync(
                chunk.Select(path => (Ulid.NewUlid().ToString(), path)).ToList(),
                thumbnailsDir,
                settings.Analysis,
                cancellationToken);

            string? lastName = null;
            string? lastFile = null;
            var batchCreated = new List<Media>();
            var mediaMetadataToInsert = new List<MediaMetadata>();
            var mediaPagesToInsert = new List<MediaPage>();

            var now = DateTimeOffset.UtcNow;

            foreach (var item in prepared)
            {
                var (newMedia, meta, pages) = BuildPreparedMedia(item, seriesId, now);
                if (meta is not null) mediaMetadataToInsert.Add(meta);
                mediaPagesToInsert.AddRange(pages);

                report.CreatedMedia++;
                batchCreated.Add(newMedia);
                lastName = newMedia.Name;
                lastFile = item.Path;
                done++;
            }

            if (batchCreated.Count > 0)
            {
                await InsertMediaBatchAsync(conn, batchCreated, mediaMetadataToInsert, mediaPagesToInsert, cancellationToken);
            }

            announced.AddRange(batchCreated.Select(m => CatalogDtoMapper.ToMediaDto(m, null)));

            var processed = new HashSet<string>(chunk, StringComparer.Ordinal);
            pendingNames.RemoveAll(processed.Contains);

            await PublishProgressAsync(progress.LibraryId, ScanPhase.ProcessingMedia, cancellationToken, new() { CompletedSeries = progress.CompletedSeries, TotalSeries = progress.TotalSeries, CurrentSeries = progress.SeriesName, CompletedMedia = done, TotalMedia = mediaToCreate.Count, PendingMedia = pendingNames.ToList(), CurrentFile = lastFile, CreatedMedia = announced.ToList(), CurrentMedia = lastName, CurrentSeriesId = progress.SeriesId });
        }
    }

    private static (Media Media, MediaMetadata? Metadata, List<MediaPage> Pages) BuildPreparedMedia(
        PreparedMedia preparedItem,
        string seriesId,
        DateTimeOffset now)
    {
        var (newMediaId, mediaPath, analyzed, thumbPath) = preparedItem;
        var fileInfo = new FileInfo(mediaPath);
        var ext = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(mediaPath);

        var newMedia = new Media
        {
            Id = newMediaId,
            Name = analyzed.Metadata?.Title ?? name,
            SortName = Core.Filesystem.SortKey.From(analyzed.Metadata?.Title ?? name),
            Path = mediaPath,
            Extension = ext,
            Size = fileInfo.Exists ? fileInfo.Length : 0,
            Pages = analyzed.Pages,
            Hash = analyzed.Hash,
            KoreaderHash = analyzed.KoreaderHash,
            ThumbnailPath = thumbPath,
            Status = FileStatus.Ready,
            SeriesId = seriesId,
            ModifiedAt = fileInfo.Exists ? new DateTimeOffset(fileInfo.LastWriteTimeUtc) : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        MediaMetadata? meta = null;
        if (analyzed.Metadata is not null)
        {
            meta = MapMediaMetadata(analyzed.Metadata);
            meta.MediaId = newMediaId;
            newMedia.Metadata = meta;
        }

        var pages = analyzed.PageDimensions.Select(p => new MediaPage
        {
            MediaId = newMediaId,
            Number = p.Number,
            FileName = p.FileName,
            MediaType = p.MediaType,
            Width = p.Width,
            Height = p.Height,
            SizeBytes = p.SizeBytes
        }).ToList();

        return (newMedia, meta, pages);
    }

    private static async Task InsertMediaBatchAsync(
        SqliteConnection conn,
        IReadOnlyList<Media> batchCreated,
        IReadOnlyList<MediaMetadata> metadataToInsert,
        IReadOnlyList<MediaPage> pagesToInsert,
        CancellationToken cancellationToken)
    {
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        const string insertMediaSql = """
            INSERT INTO "Media" (
                "Id", "Name", "SortName", "Size", "Extension", "Pages", "Path", "Status",
                "Hash", "KoreaderHash", "ThumbnailPath", "ThumbnailMeta", "CreatedAt", "UpdatedAt", "ModifiedAt", "DeletedAt", "SeriesId"
            ) VALUES (
                @Id, @Name, @SortName, @Size, @Extension, @Pages, @Path, @Status,
                @Hash, @KoreaderHash, @ThumbnailPath, @ThumbnailMeta, @CreatedAt, @UpdatedAt, @ModifiedAt, @DeletedAt, @SeriesId
            );
            """;
        await conn.ExecuteAsync(insertMediaSql, batchCreated.Select(m => new
        {
            m.Id,
            m.Name,
            m.SortName,
            m.Size,
            m.Extension,
            m.Pages,
            m.Path,
            Status = m.Status.ToString(),
            m.Hash,
            m.KoreaderHash,
            m.ThumbnailPath,
            m.ThumbnailMeta,
            m.CreatedAt,
            m.UpdatedAt,
            m.ModifiedAt,
            m.DeletedAt,
            m.SeriesId
        }), tx);

        if (metadataToInsert.Count > 0)
        {
            const string insertMetaSql = """
                INSERT INTO "MediaMetadata" (
                    "MediaId", "AgeRating", "Summary", "Publisher", "Genres", "Number", "Volume",
                    "Year", "Month", "Day", "Writers", "Pencillers", "Inkers", "Colorists", "Letterers", "CoverArtists", "Editors"
                ) VALUES (
                    @MediaId, @AgeRating, @Summary, @Publisher, @Genres, @Number, @Volume,
                    @Year, @Month, @Day, @Writers, @Pencillers, @Inkers, @Colorists, @Letterers, @CoverArtists, @Editors
                );
                """;
            await conn.ExecuteAsync(insertMetaSql, metadataToInsert, tx);
        }

        if (pagesToInsert.Count > 0)
        {
            const string insertPagesSql = """
                INSERT INTO "MediaPages" ("MediaId", "Number", "FileName", "MediaType", "Width", "Height", "SizeBytes")
                VALUES (@MediaId, @Number, @FileName, @MediaType, @Width, @Height, @SizeBytes);
                """;
            await conn.ExecuteAsync(insertPagesSql, pagesToInsert, tx);
        }

        await tx.CommitAsync(cancellationToken);
    }
}
