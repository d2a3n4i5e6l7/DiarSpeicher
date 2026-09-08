using System.Diagnostics;
using System.IO;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<LibraryScannerService> _logger;

    public LibraryScannerService(
        DiarSpeicherDbContext dbContext,
        IDirectoryScanner directoryScanner,
        ICompositeBookProcessor bookProcessor,
        IThumbnailService thumbnailService,
        ILogger<LibraryScannerService> logger)
    {
        _dbContext = dbContext;
        _directoryScanner = directoryScanner;
        _bookProcessor = bookProcessor;
        _thumbnailService = thumbnailService;
        _logger = logger;
    }

    public async Task<LibraryScanReport> ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new LibraryScanReport { LibraryId = libraryId };

        _logger.LogInformation("Starting scan for library {LibraryId}", libraryId);

        var library = await _dbContext.Libraries
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == libraryId, cancellationToken);

        if (library is null)
        {
            _logger.LogError("Library {LibraryId} not found in database", libraryId);
            report.Success = false;
            report.ErrorMessage = $"Library {libraryId} not found";
            report.Duration = sw.Elapsed;
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
            return report;
        }

        if (library.Status.IsRecoveredIfPresent())
        {
            library.Status = FileStatus.Ready;
        }

        var isCollectionBased = library.Config?.LibraryPattern == LibraryPattern.CollectionBased;
        var thumbnailsDir = Path.Combine(Directory.GetCurrentDirectory(), "storage", "thumbnails");

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
            report,
            cancellationToken);

        // 5. Batch Upsert ScannedDirectory MTimes
        await UpsertScannedDirMtimesAsync(allObservedDirMtimes, cancellationToken);

        report.Duration = sw.Elapsed;
        report.Success = true;

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
                Id = Guid.NewGuid().ToString(),
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
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        var distinctPaths = seriesPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var seriesEntities = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId && distinctPaths.Contains(s.Path))
            .ToListAsync(cancellationToken);

        var allObservedDirMtimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var series in seriesEntities)
        {
            await ProcessSingleSeriesAsync(series, storedMtimes, thumbnailsDir, allObservedDirMtimes, report, cancellationToken);
        }

        return allObservedDirMtimes;
    }

    private async Task ProcessSingleSeriesAsync(
        Series series,
        IReadOnlyDictionary<string, long> storedMtimes,
        string thumbnailsDir,
        Dictionary<string, long> allObservedDirMtimes,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
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

        await MarkMissingMediaAsync(series.Id, walkedSeries.MissingMedia, report, cancellationToken);
        await MarkRecoveredMediaAsync(walkedSeries.RecoveredMedia, report, cancellationToken);
        await CreateNewMediaAsync(series.Id, walkedSeries.MediaToCreate, thumbnailsDir, report, cancellationToken);
        await UpdateVisitedMediaAsync(series.Id, walkedSeries.MediaToVisit, thumbnailsDir, report, cancellationToken);

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

    private async Task CreateNewMediaAsync(
        string seriesId,
        List<string> mediaToCreate,
        string thumbnailsDir,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        foreach (var mediaPath in mediaToCreate)
        {
            var fileInfo = new FileInfo(mediaPath);
            var ext = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
            var name = Path.GetFileNameWithoutExtension(mediaPath);

            var analyzed = await _bookProcessor.AnalyzeAsync(mediaPath, cancellationToken);
            var newMediaId = Guid.NewGuid().ToString();
            var thumbPath = await _thumbnailService.GenerateThumbnailAsync(newMediaId, mediaPath, thumbnailsDir, cancellationToken);

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
            report.CreatedMedia++;
        }
    }

    private async Task UpdateVisitedMediaAsync(
        string seriesId,
        List<string> toVisitPaths,
        string thumbnailsDir,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        if (toVisitPaths.Count == 0) return;

        var mediaToUpdate = await _dbContext.Media
            .Where(m => m.SeriesId == seriesId && toVisitPaths.Contains(m.Path))
            .ToListAsync(cancellationToken);

        foreach (var m in mediaToUpdate)
        {
            var fileInfo = new FileInfo(m.Path);
            if (!fileInfo.Exists) continue;

            var analyzed = await _bookProcessor.AnalyzeAsync(m.Path, cancellationToken);
            var thumbPath = await _thumbnailService.GenerateThumbnailAsync(m.Id, m.Path, thumbnailsDir, cancellationToken);

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
            report.UpdatedMedia++;
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
