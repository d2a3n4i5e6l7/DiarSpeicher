using System.Diagnostics;
using System.IO;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
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
    private readonly ILogger<LibraryScannerService> _logger;

    public LibraryScannerService(
        DiarSpeicherDbContext dbContext,
        IDirectoryScanner directoryScanner,
        ILogger<LibraryScannerService> logger)
    {
        _dbContext = dbContext;
        _directoryScanner = directoryScanner;
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

        // 1. Load cached directory mtimes for this library path
        var storedMtimes = await _dbContext.ScannedDirectories
            .Where(sd => sd.Path.StartsWith(libraryPath))
            .AsNoTracking()
            .ToDictionaryAsync(sd => sd.Path, sd => sd.LastMTime, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // 2. Load existing series records for this library
        var existingSeries = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId)
            .Select(s => new ExistingSeriesInfo(s.Id, s.Path, s.Status))
            .ToListAsync(cancellationToken);

        // 3. Walk library
        var walkedLibrary = _directoryScanner.WalkLibrary(libraryPath, isCollectionBased, existingSeries);
        report.TotalDirectories = walkedLibrary.SeenDirectories;
        report.IgnoredDirectories = walkedLibrary.IgnoredDirectories;

        // 4. Handle Missing Series
        if (walkedLibrary.MissingSeries.Count > 0)
        {
            var missingPaths = walkedLibrary.MissingSeries;
            var seriesToMarkMissing = await _dbContext.Series
                .Where(s => s.LibraryId == libraryId && missingPaths.Contains(s.Path))
                .ToListAsync(cancellationToken);

            foreach (var s in seriesToMarkMissing)
            {
                s.Status = FileStatus.Missing;
                report.UpdatedSeries++;

                // Mark all media in missing series as missing too (matching Stump handle_missing_series)
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

        // 5. Handle Recovered Series
        if (walkedLibrary.RecoveredSeries.Count > 0)
        {
            var recoveredIds = walkedLibrary.RecoveredSeries;
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

        // 6. Handle Series to Create
        var newlyCreatedSeries = new List<Series>();
        foreach (var newSeriesPath in walkedLibrary.SeriesToCreate)
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Series.Add(newSeries);
            newlyCreatedSeries.Add(newSeries);
            report.CreatedSeries++;
        }

        if (newlyCreatedSeries.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 7. Series to Visit (Existing Series to visit + Newly Created Series)
        var allSeriesToVisitPaths = walkedLibrary.SeriesToVisit
            .Concat(newlyCreatedSeries.Select(s => s.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seriesEntities = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId && allSeriesToVisitPaths.Contains(s.Path))
            .ToListAsync(cancellationToken);

        var allObservedDirMtimes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // 8. Walk each series
        foreach (var series in seriesEntities)
        {
            var existingMedia = await _dbContext.Media
                .Where(m => m.SeriesId == series.Id)
                .Select(m => new ExistingMediaInfo(m.Id, m.Path, m.Status, m.ModifiedAt))
                .ToListAsync(cancellationToken);

            var walkedSeries = _directoryScanner.WalkSeries(series.Path, storedMtimes, existingMedia);

            report.TotalFiles += walkedSeries.SeenFiles;
            report.IgnoredFiles += walkedSeries.IgnoredFiles;
            report.SkippedFiles += walkedSeries.SkippedFiles;

            // Merge observed mtimes
            foreach (var (dirPath, mtime) in walkedSeries.ObservedDirMtimes)
            {
                allObservedDirMtimes[dirPath] = mtime;
            }

            // Handle Missing Media
            if (walkedSeries.MissingMedia.Count > 0)
            {
                var missingPaths = walkedSeries.MissingMedia;
                var mediaToMarkMissing = await _dbContext.Media
                    .Where(m => m.SeriesId == series.Id && missingPaths.Contains(m.Path))
                    .ToListAsync(cancellationToken);

                foreach (var m in mediaToMarkMissing)
                {
                    m.Status = FileStatus.Missing;
                    report.UpdatedMedia++;
                }
            }

            // Handle Recovered Media
            if (walkedSeries.RecoveredMedia.Count > 0)
            {
                var recoveredMediaIds = walkedSeries.RecoveredMedia;
                var mediaToRecover = await _dbContext.Media
                    .Where(m => recoveredMediaIds.Contains(m.Id))
                    .ToListAsync(cancellationToken);

                foreach (var m in mediaToRecover)
                {
                    m.Status = FileStatus.Ready;
                    report.UpdatedMedia++;
                }
            }

            // Handle Media to Create
            foreach (var mediaPath in walkedSeries.MediaToCreate)
            {
                var fileInfo = new FileInfo(mediaPath);
                var ext = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
                var name = Path.GetFileNameWithoutExtension(mediaPath);

                var newMedia = new Media
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Path = mediaPath,
                    Extension = ext,
                    Size = fileInfo.Exists ? fileInfo.Length : 0,
                    Pages = 0,
                    Status = FileStatus.Ready,
                    SeriesId = series.Id,
                    ModifiedAt = fileInfo.Exists ? new DateTimeOffset(fileInfo.LastWriteTimeUtc) : null,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                _dbContext.Media.Add(newMedia);
                report.CreatedMedia++;
            }

            // Handle Media to Visit (updated files)
            if (walkedSeries.MediaToVisit.Count > 0)
            {
                var toVisitPaths = walkedSeries.MediaToVisit;
                var mediaToUpdate = await _dbContext.Media
                    .Where(m => m.SeriesId == series.Id && toVisitPaths.Contains(m.Path))
                    .ToListAsync(cancellationToken);

                foreach (var m in mediaToUpdate)
                {
                    var fileInfo = new FileInfo(m.Path);
                    if (fileInfo.Exists)
                    {
                        m.Size = fileInfo.Length;
                        m.ModifiedAt = new DateTimeOffset(fileInfo.LastWriteTimeUtc);
                        m.Status = FileStatus.Ready;
                        m.UpdatedAt = DateTimeOffset.UtcNow;
                        report.UpdatedMedia++;
                    }
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 9. Batch Upsert ScannedDirectory MTimes (matching Stump finalization)
        if (allObservedDirMtimes.Count > 0)
        {
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
}
