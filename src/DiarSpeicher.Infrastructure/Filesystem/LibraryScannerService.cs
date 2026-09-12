using System.Diagnostics;
using System.IO;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.EntityFrameworkCore;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Filesystem;

public interface ILibraryScannerService
{
    Task<LibraryScanReport> ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default);
}

public class LibraryScannerService : ILibraryScannerService
{
    private readonly DiarSpeicherDbContext _dbContext;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly IThumbnailService _thumbnailService;
    private readonly IArchiveConversionService _archiveConverter;
    private readonly StorageOptions _storage;
    private readonly ILogger<LibraryScannerService> _logger;
    private readonly IScanProgressPublisher? _progressPublisher;

    /// <summary>
    /// Archive decompression and hashing are CPU-bound, so the analysis phase is spread
    /// across cores. The database phase stays single-threaded: DbContext is not thread-safe.
    /// </summary>
    private static readonly int AnalysisParallelism = Math.Max(1, Environment.ProcessorCount - 1);

    private sealed record PreparedMedia(string MediaId, string Path, ProcessedBook Analysis, string? ThumbnailPath);

    /// <summary>
    /// Lo que la configuracion de la biblioteca le pide a este escaneo. Se resuelve una vez
    /// al principio y viaja hacia abajo: leerla dentro del bucle obligaria a arrastrar la
    /// entidad Library por toda la cadena solo para consultar cuatro booleanos.
    /// </summary>
    private sealed record ScanSettings(BookAnalysisOptions Analysis, bool ConvertRarToZip, bool HardDeleteConversions)
    {
        public static ScanSettings From(LibraryConfig? config) => new(
            new BookAnalysisOptions
            {
                ComputeFileHash = config?.GenerateFileHashes ?? true,
                ComputeKoreaderHash = config?.GenerateKoreaderHashes ?? true,
                ReadEmbeddedMetadata = config?.ProcessMetadata ?? true
            },
            config?.ConvertRarToZip ?? false,
            config?.HardDeleteConversions ?? false);
    }

    /// <summary>
    /// Un CBR con un CBZ hermano es el mismo libro dos veces. Se descarta el CBR, venga de
    /// una conversion que no borro el original o de una copia que ya estaba ahi.
    /// </summary>
    private static bool HasConvertedSibling(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".cbr", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sibling = Path.ChangeExtension(path, ".cbz");
        return sibling is not null && File.Exists(sibling);
    }

    public LibraryScannerService(
        DiarSpeicherDbContext dbContext,
        IDirectoryScanner directoryScanner,
        ICompositeBookProcessor bookProcessor,
        IThumbnailService thumbnailService,
        IArchiveConversionService archiveConverter,
        ILogger<LibraryScannerService> logger,
        IOptions<StorageOptions>? storageOptions = null,
        IScanProgressPublisher? progressPublisher = null)
    {
        _dbContext = dbContext;
        _directoryScanner = directoryScanner;
        _bookProcessor = bookProcessor;
        _thumbnailService = thumbnailService;
        _archiveConverter = archiveConverter;
        _storage = storageOptions?.Value ?? new StorageOptions();
        _logger = logger;
        _progressPublisher = progressPublisher;
    }

    private ValueTask PublishProgressAsync(
        string libraryId,
        ScanPhase phase,
        CancellationToken cancellationToken,
        int completedSeries = 0,
        int totalSeries = 0,
        string? currentSeries = null,
        string? message = null)
    {
        if (_progressPublisher is null)
        {
            return ValueTask.CompletedTask;
        }

        return _progressPublisher.PublishAsync(new ScanProgressEvent
        {
            JobId = libraryId,
            LibraryId = libraryId,
            Phase = phase,
            CompletedSeries = completedSeries,
            TotalSeries = totalSeries,
            CurrentSeries = currentSeries,
            Message = message
        }, cancellationToken);
    }

    public async Task<LibraryScanReport> ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new LibraryScanReport { LibraryId = libraryId };

        _logger.LogInformation("Starting scan for library {LibraryId}", libraryId);
        await PublishProgressAsync(libraryId, ScanPhase.Started, cancellationToken);

        var library = await _dbContext.Libraries
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == libraryId, cancellationToken);

        if (library is null)
        {
            _logger.LogError("Library {LibraryId} not found in database", libraryId);
            report.Success = false;
            report.ErrorMessage = $"Library {libraryId} not found";
            report.Duration = sw.Elapsed;
            await PublishProgressAsync(libraryId, ScanPhase.Failed, cancellationToken, message: report.ErrorMessage);
            return report;
        }

        var libraryPath = Path.GetFullPath(library.Path);
        if (!Directory.Exists(libraryPath))
        {
            _logger.LogWarning("Library path {Path} does not exist on disk", libraryPath);
            library.Status = FileStatus.Missing;
            await _dbContext.SaveChangesAsync(cancellationToken);

            report.Success = false;
            report.ErrorMessage = $"Library directory {libraryPath} does not exist";
            report.Duration = sw.Elapsed;
            await PublishProgressAsync(libraryId, ScanPhase.Failed, cancellationToken, message: report.ErrorMessage);
            return report;
        }

        if (library.Status.IsRecoveredIfPresent())
        {
            library.Status = FileStatus.Ready;
        }

        var isCollectionBased = library.Config?.LibraryPattern == LibraryPattern.CollectionBased;
        var settings = ScanSettings.From(library.Config);
        var thumbnailsDir = _storage.ResolveThumbnailsPath();
        Directory.CreateDirectory(thumbnailsDir);

        // 1. Load cached directory mtimes and existing series
        var storedMtimes = await _dbContext.ScannedDirectories
            .Where(sd => sd.Path.StartsWith(libraryPath))
            .AsNoTracking()
            .ToDictionaryAsync(sd => sd.Path, sd => sd.LastMTime, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var existingSeries = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId)
            .Select(s => new ExistingSeriesInfo(s.Id, s.Path, s.Status))
            .ToListAsync(cancellationToken);

        // 2. Walk library
        await PublishProgressAsync(libraryId, ScanPhase.WalkingLibrary, cancellationToken);
        var walkedLibrary = _directoryScanner.WalkLibrary(libraryPath, isCollectionBased, existingSeries);
        report.TotalDirectories = walkedLibrary.SeenDirectories;
        report.IgnoredDirectories = walkedLibrary.IgnoredDirectories;

        // 3. Reconcile missing, recovered, and new series
        await ProcessMissingSeriesAsync(libraryId, walkedLibrary.MissingSeries, report, cancellationToken);
        await ProcessRecoveredSeriesAsync(walkedLibrary.RecoveredSeries, report, cancellationToken);
        var newlyCreatedSeries = await CreateNewSeriesAsync(libraryId, libraryPath, walkedLibrary.SeriesToCreate, report, cancellationToken);

        // 4. Walk series and process media
        var allObservedDirMtimes = await ProcessAllSeriesAsync(
            libraryId,
            walkedLibrary.SeriesToVisit.Concat(newlyCreatedSeries.Select(s => s.Path)),
            storedMtimes,
            thumbnailsDir,
            settings,
            report,
            cancellationToken);

        // 5. Batch Upsert ScannedDirectory MTimes
        await UpsertScannedDirMtimesAsync(allObservedDirMtimes, cancellationToken);

        report.Duration = sw.Elapsed;
        report.Success = true;

        await PublishProgressAsync(libraryId, ScanPhase.Completed, cancellationToken, message: "Scan complete");

        _logger.LogInformation(
            "Scan complete for library {LibraryId} in {ElapsedMs}ms. Created {CreatedSeries} series, {CreatedMedia} media. Updated {UpdatedSeries} series, {UpdatedMedia} media. Skipped {SkippedFiles} files.",
            libraryId,
            sw.ElapsedMilliseconds,
            report.CreatedSeries,
            report.CreatedMedia,
            report.UpdatedSeries,
            report.UpdatedMedia,
            report.SkippedFiles);

        return report;
    }

    private async Task ProcessMissingSeriesAsync(string libraryId, List<string> missingPaths, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (missingPaths.Count == 0) return;

        var seriesToMarkMissing = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId && missingPaths.Contains(s.Path))
            .ToListAsync(cancellationToken);

        foreach (var s in seriesToMarkMissing)
        {
            s.Status = FileStatus.Missing;
            report.UpdatedSeries++;

            var mediaInMissingSeries = await _dbContext.Media
                .Where(m => m.SeriesId == s.Id)
                .ToListAsync(cancellationToken);

            foreach (var m in mediaInMissingSeries)
            {
                m.Status = FileStatus.Missing;
                report.UpdatedMedia++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessRecoveredSeriesAsync(List<string> recoveredIds, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (recoveredIds.Count == 0) return;

        var seriesToRecover = await _dbContext.Series
            .Where(s => recoveredIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        foreach (var s in seriesToRecover)
        {
            s.Status = FileStatus.Ready;
            report.UpdatedSeries++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<Series>> CreateNewSeriesAsync(
        string libraryId,
        string libraryPath,
        List<string> seriesToCreate,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        var newlyCreated = new List<Series>();
        foreach (var newSeriesPath in seriesToCreate)
        {
            var dirName = Path.GetFileName(newSeriesPath);
            if (string.IsNullOrEmpty(dirName))
            {
                dirName = Path.GetFileName(libraryPath);
            }

            var newSeries = new Series
            {
                Name = dirName,
                Path = newSeriesPath,
                LibraryId = libraryId,
                Status = FileStatus.Ready,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.Series.Add(newSeries);
            newlyCreated.Add(newSeries);
            report.CreatedSeries++;
        }

        if (newlyCreated.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return newlyCreated;
    }

    private async Task<Dictionary<string, long>> ProcessAllSeriesAsync(
        string libraryId,
        IEnumerable<string> seriesPaths,
        IReadOnlyDictionary<string, long> storedMtimes,
        string thumbnailsDir,
        ScanSettings settings,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        var distinctPaths = seriesPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var seriesEntities = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId && distinctPaths.Contains(s.Path))
            .ToListAsync(cancellationToken);

        var allObservedDirMtimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var completed = 0;

        foreach (var series in seriesEntities)
        {
            await PublishProgressAsync(
                libraryId,
                ScanPhase.ProcessingSeries,
                cancellationToken,
                completedSeries: completed,
                totalSeries: seriesEntities.Count,
                currentSeries: series.Name);

            await ProcessSingleSeriesAsync(series, storedMtimes, thumbnailsDir, allObservedDirMtimes, settings, report, cancellationToken);
            completed++;
        }

        return allObservedDirMtimes;
    }

    private async Task ProcessSingleSeriesAsync(
        Series series,
        IReadOnlyDictionary<string, long> storedMtimes,
        string thumbnailsDir,
        Dictionary<string, long> allObservedDirMtimes,
        ScanSettings settings,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        // La conversion va antes de mirar el disco: si no, el recorrido veria el CBR que
        // esta a punto de desaparecer y el CBZ que aun no existe.
        if (settings.ConvertRarToZip)
        {
            var conversions = await _archiveConverter.ConvertDirectoryAsync(
                series.Path,
                settings.HardDeleteConversions,
                cancellationToken);

            await RepointConvertedMediaAsync(series.Id, conversions, report, cancellationToken);
        }

        var existingMedia = await _dbContext.Media
            .Where(m => m.SeriesId == series.Id)
            .Select(m => new ExistingMediaInfo(m.Id, m.Path, m.Status, m.ModifiedAt))
            .ToListAsync(cancellationToken);

        var walkedSeries = _directoryScanner.WalkSeries(series.Path, storedMtimes, existingMedia);

        report.TotalFiles += walkedSeries.SeenFiles;
        report.IgnoredFiles += walkedSeries.IgnoredFiles;
        report.SkippedFiles += walkedSeries.SkippedFiles;

        foreach (var (dirPath, mtime) in walkedSeries.ObservedDirMtimes)
        {
            allObservedDirMtimes[dirPath] = mtime;
        }

        var mediaToCreate = walkedSeries.MediaToCreate.Where(path => !HasConvertedSibling(path)).ToList();

        await MarkMissingMediaAsync(series.Id, walkedSeries.MissingMedia, report, cancellationToken);
        await MarkRecoveredMediaAsync(walkedSeries.RecoveredMedia, report, cancellationToken);
        await CreateNewMediaAsync(series.Id, mediaToCreate, thumbnailsDir, settings, report, cancellationToken);
        await UpdateVisitedMediaAsync(series.Id, walkedSeries.MediaToVisit, thumbnailsDir, settings, report, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Mueve el medio ya indexado al CBZ resultante. Sin esto el CBR quedaria marcado como
    /// desaparecido y el CBZ entraria como libro nuevo, y con el cambio el usuario perderia
    /// el progreso de lectura que colgaba del medio anterior.
    /// </summary>
    private async Task RepointConvertedMediaAsync(
        string seriesId,
        IReadOnlyList<ArchiveConversion> conversions,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        if (conversions.Count == 0) return;

        var sourcePaths = conversions.Select(c => c.SourcePath).ToList();
        var affected = await _dbContext.Media
            .Where(m => m.SeriesId == seriesId && sourcePaths.Contains(m.Path))
            .ToListAsync(cancellationToken);

        if (affected.Count == 0) return;

        var bySource = conversions.ToDictionary(c => c.SourcePath, StringComparer.OrdinalIgnoreCase);

        foreach (var media in affected)
        {
            if (!bySource.TryGetValue(media.Path, out var conversion)) continue;

            media.Path = conversion.TargetPath;
            media.Extension = "cbz";
            media.Status = FileStatus.Ready;
            media.UpdatedAt = DateTimeOffset.UtcNow;
            report.UpdatedMedia++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkMissingMediaAsync(string seriesId, List<string> missingPaths, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (missingPaths.Count == 0) return;

        var mediaToMarkMissing = await _dbContext.Media
            .Where(m => m.SeriesId == seriesId && missingPaths.Contains(m.Path))
            .ToListAsync(cancellationToken);

        foreach (var m in mediaToMarkMissing)
        {
            m.Status = FileStatus.Missing;
            report.UpdatedMedia++;
        }
    }

    private async Task MarkRecoveredMediaAsync(List<string> recoveredIds, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (recoveredIds.Count == 0) return;

        var mediaToRecover = await _dbContext.Media
            .Where(m => recoveredIds.Contains(m.Id))
            .ToListAsync(cancellationToken);

        foreach (var m in mediaToRecover)
        {
            m.Status = FileStatus.Ready;
            report.UpdatedMedia++;
        }
    }

    /// <summary>
    /// Parallel phase of a scan: opens, decompresses and hashes each book and writes its
    /// thumbnail. Deliberately touches no DbContext state, so it is safe to fan out.
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
                    var analyzed = await _bookProcessor.AnalyzeAsync(mediaPath, includeCover: true, token, measurePages: true, analysisOptions);
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
        CancellationToken cancellationToken)
    {
        if (mediaToCreate.Count == 0) return;

        var prepared = await PrepareMediaAsync(
            mediaToCreate.Select(path => (Ulid.NewUlid().ToString(), path)).ToList(),
            thumbnailsDir,
            settings.Analysis,
            cancellationToken);

        // Sequential phase: the change tracker must only ever be touched by one thread.
        foreach (var (newMediaId, mediaPath, analyzed, thumbPath) in prepared)
        {
            var fileInfo = new FileInfo(mediaPath);
            var ext = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
            var name = Path.GetFileNameWithoutExtension(mediaPath);

            var newMedia = new Media
            {
                Id = newMediaId,
                Name = analyzed.Metadata?.Title ?? name,
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
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (analyzed.Metadata is not null)
            {
                newMedia.Metadata = MapMediaMetadata(analyzed.Metadata);
            }

            _dbContext.Media.Add(newMedia);
            AddPageDimensions(newMediaId, analyzed);
            report.CreatedMedia++;
        }
    }

    private async Task UpdateVisitedMediaAsync(
        string seriesId,
        List<string> toVisitPaths,
        string thumbnailsDir,
        ScanSettings settings,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        if (toVisitPaths.Count == 0) return;

        var mediaToUpdate = await _dbContext.Media
            .Where(m => m.SeriesId == seriesId && toVisitPaths.Contains(m.Path))
            .ToListAsync(cancellationToken);

        var existingOnDisk = mediaToUpdate.Where(m => File.Exists(m.Path)).ToList();
        if (existingOnDisk.Count == 0) return;

        var prepared = await PrepareMediaAsync(
            existingOnDisk.Select(m => (m.Id, m.Path)).ToList(),
            thumbnailsDir,
            settings.Analysis,
            cancellationToken);

        var byId = prepared.ToDictionary(p => p.MediaId, StringComparer.Ordinal);

        // Sequential phase: the change tracker must only ever be touched by one thread.
        foreach (var m in existingOnDisk)
        {
            if (!byId.TryGetValue(m.Id, out var result)) continue;

            var fileInfo = new FileInfo(m.Path);
            if (!fileInfo.Exists) continue;

            var analyzed = result.Analysis;
            var thumbPath = result.ThumbnailPath;

            m.Size = fileInfo.Length;
            m.Pages = analyzed.Pages;
            m.Hash = analyzed.Hash;
            m.KoreaderHash = analyzed.KoreaderHash;
            if (!string.IsNullOrEmpty(thumbPath))
            {
                m.ThumbnailPath = thumbPath;
            }
            m.ModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
            m.Status = FileStatus.Ready;
            m.UpdatedAt = DateTimeOffset.UtcNow;

            var staleDimensions = await _dbContext.MediaPages
                .Where(mp => mp.MediaId == m.Id)
                .ToListAsync(cancellationToken);
            if (staleDimensions.Count > 0)
            {
                _dbContext.MediaPages.RemoveRange(staleDimensions);
            }
            AddPageDimensions(m.Id, analyzed);

            report.UpdatedMedia++;
        }
    }

    /// <summary>
    /// Guarda las dimensiones que el analisis ya midio. Se persisten en el scan porque los
    /// clientes de Komga las exigen para abrir el libro, y calcularlas en la peticion del
    /// lector obliga a descomprimir el libro entero con el usuario esperando.
    /// </summary>
    private void AddPageDimensions(string mediaId, ProcessedBook analyzed)
    {
        if (analyzed.PageDimensions.Count == 0) return;

        foreach (var page in analyzed.PageDimensions)
        {
            _dbContext.MediaPages.Add(new MediaPage
            {
                MediaId = mediaId,
                Number = page.Number,
                FileName = page.FileName,
                MediaType = page.MediaType,
                Width = page.Width,
                Height = page.Height,
                SizeBytes = page.SizeBytes
            });
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

    private async Task UpsertScannedDirMtimesAsync(Dictionary<string, long> allObservedDirMtimes, CancellationToken cancellationToken)
    {
        if (allObservedDirMtimes.Count == 0) return;

        var observedPaths = allObservedDirMtimes.Keys.ToList();
        var existingScannedDirs = await _dbContext.ScannedDirectories
            .Where(sd => observedPaths.Contains(sd.Path))
            .ToDictionaryAsync(sd => sd.Path, sd => sd, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var (dirPath, mtime) in allObservedDirMtimes)
        {
            if (existingScannedDirs.TryGetValue(dirPath, out var sd))
            {
                sd.LastMTime = mtime;
            }
            else
            {
                _dbContext.ScannedDirectories.Add(new ScannedDirectory
                {
                    Path = dirPath,
                    LastMTime = mtime
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
