using System.Diagnostics;
using System.IO;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;

namespace DiarSpeicher.Infrastructure.Filesystem;

/// <summary>Lo que el escaner usa si esta disponible pero no necesita para funcionar.</summary>
public sealed class LibraryScannerOptions
{
    public IOptions<StorageOptions>? Storage { get; init; }
    public IScanProgressPublisher? ProgressPublisher { get; init; }
}

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
    /// Lo que hace falta para situar un tomo dentro del escaneo completo. Viaja hacia abajo
    /// porque quien conoce el avance por tomos es el bucle de creacion, y quien conoce el
    /// avance por series esta tres niveles mas arriba.
    /// </summary>
    /// <summary>
    /// Lo invariante de un escaneo. Viajaba como siete parametros sueltos por toda la cadena,
    /// y cada metodo nuevo tenia que volver a enumerarlos para pasarlos al siguiente.
    /// </summary>
    private sealed record ScanContext(
        string LibraryId,
        IReadOnlyDictionary<string, long> StoredMtimes,
        string ThumbnailsDir,
        ScanSettings Settings,
        LibraryScanReport Report)
    {
        public Dictionary<string, long> ObservedDirMtimes { get; } = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> AllSeriesPaths { get; set; } = [];
    }

    private sealed record SeriesProgress(string LibraryId, string SeriesId, string SeriesName, int CompletedSeries, int TotalSeries);

    /// <summary>
    /// Lo que la configuracion de la biblioteca le pide a este escaneo. Se resuelve una vez
    /// al principio y viaja hacia abajo: leerla dentro del bucle obligaria a arrastrar la
    /// entidad Library por toda la cadena solo para consultar cuatro booleanos.
    /// </summary>
    private sealed record ScanSettings(BookAnalysisOptions Analysis, bool ConvertRarToZip, bool HardDeleteConversions)
    {
        public static ScanSettings From(LibraryConfig? config)
        {
            if (config is null)
            {
                return new(
                    new BookAnalysisOptions
                    {
                        ComputeFileHash = true,
                        ComputeKoreaderHash = true,
                        ReadEmbeddedMetadata = true
                    },
                    ConvertRarToZip: false,
                    HardDeleteConversions: false);
            }

            return new(
                new BookAnalysisOptions
                {
                    ComputeFileHash = config.GenerateFileHashes,
                    ComputeKoreaderHash = config.GenerateKoreaderHashes,
                    ReadEmbeddedMetadata = config.ProcessMetadata
                },
                config.ConvertRarToZip,
                config.HardDeleteConversions);
        }
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
        LibraryScannerOptions? options = null)
    {
        _dbContext = dbContext;
        _directoryScanner = directoryScanner;
        _bookProcessor = bookProcessor;
        _thumbnailService = thumbnailService;
        _archiveConverter = archiveConverter;
        _logger = logger;
        _storage = options?.Storage?.Value ?? new StorageOptions();
        _progressPublisher = options?.ProgressPublisher;
    }

    private sealed record ScanProgressDetail
    {
        public int CompletedSeries { get; init; }
        public int TotalSeries { get; init; }
        public string? CurrentSeries { get; init; }
        public string? Message { get; init; }
        public int CompletedMedia { get; init; }
        public int TotalMedia { get; init; }
        public string? CurrentMedia { get; init; }
        public string? CurrentSeriesId { get; init; }
    }

    private ValueTask PublishProgressAsync(string libraryId, ScanPhase phase, CancellationToken cancellationToken, ScanProgressDetail? detail = null)
    {
        if (_progressPublisher is null)
        {
            return ValueTask.CompletedTask;
        }

        var d = detail ?? new ScanProgressDetail();
        return _progressPublisher.PublishAsync(new ScanProgressEvent
        {
            JobId = libraryId,
            LibraryId = libraryId,
            Phase = phase,
            CompletedSeries = d.CompletedSeries,
            TotalSeries = d.TotalSeries,
            CurrentSeries = d.CurrentSeries,
            CompletedMedia = d.CompletedMedia,
            TotalMedia = d.TotalMedia,
            CurrentMedia = d.CurrentMedia,
            CurrentSeriesId = d.CurrentSeriesId,
            Message = d.Message
        }, cancellationToken);
    }

    private async Task<Library?> ResolveLibraryAsync(
        string libraryId,
        LibraryScanReport report,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        var library = await _dbContext.Libraries
            .Include(l => l.Config)
            .FirstOrDefaultAsync(l => l.Id == libraryId, cancellationToken);

        if (library is null)
        {
            _logger.LogError("Library {LibraryId} not found in database", libraryId);
            report.Success = false;
            report.ErrorMessage = $"Library {libraryId} not found";
            report.Duration = sw.Elapsed;
            await PublishProgressAsync(libraryId, ScanPhase.Failed, cancellationToken, new() { Message = report.ErrorMessage });
            return null;
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
            await PublishProgressAsync(libraryId, ScanPhase.Failed, cancellationToken, new() { Message = report.ErrorMessage });
            return null;
        }

        if (library.Status.IsRecoveredIfPresent())
        {
            library.Status = FileStatus.Ready;
        }

        return library;
    }

    public async Task<LibraryScanReport> ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new LibraryScanReport { LibraryId = libraryId };

        _logger.LogInformation("Starting scan for library {LibraryId}", libraryId);
        await PublishProgressAsync(libraryId, ScanPhase.Started, cancellationToken);

        var library = await ResolveLibraryAsync(libraryId, report, sw, cancellationToken);
        if (library is null) return report;

        var libraryPath = Path.GetFullPath(library.Path);

        var isCollectionBased = library.Config?.LibraryPattern == LibraryPattern.CollectionBased;
        var settings = ScanSettings.From(library.Config);
        var thumbnailsDir = _storage.ResolveThumbnailsPath();
        Directory.CreateDirectory(thumbnailsDir);

        await PublishProgressAsync(libraryId, ScanPhase.Started, cancellationToken, new() { Message = "Leyendo el indice anterior" });

        var storedMtimes = await LoadDirectoryMtimeCacheAsync(libraryPath, cancellationToken);

        var existingSeries = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId)
            .Select(s => new ExistingSeriesInfo(s.Id, s.Path, s.Status))
            .ToListAsync(cancellationToken);

        await PublishProgressAsync(libraryId, ScanPhase.WalkingLibrary, cancellationToken, new() { Message = "Recorriendo carpetas en el disco" });
        var walkedLibrary = _directoryScanner.WalkLibrary(libraryPath, isCollectionBased, existingSeries);
        report.TotalDirectories = walkedLibrary.SeenDirectories;
        report.IgnoredDirectories = walkedLibrary.IgnoredDirectories;

        await PublishProgressAsync(libraryId, ScanPhase.WalkingLibrary, cancellationToken, new()
        {
            TotalSeries = walkedLibrary.SeriesToCreate.Count + walkedLibrary.SeriesToVisit.Count,
            Message = $"Cotejando con el indice: {walkedLibrary.MissingSeries.Count} sin carpeta, {walkedLibrary.SeriesToCreate.Count} nuevas"
        });


        if (existingSeries.Count > 0
            && walkedLibrary.SeriesToVisit.Count == 0
            && walkedLibrary.SeriesToCreate.Count == 0)
        {
            _logger.LogWarning(
                "La biblioteca {LibraryId} tiene {Count} series en el indice y el disco no devuelve ninguna en {Path}. "
                + "No se marca nada como desaparecido: parece un montaje ausente.",
                libraryId, existingSeries.Count, libraryPath);

            report.Success = true;
            report.ErrorMessage = "El disco no devolvio ninguna serie; el indice se deja intacto.";
            report.Duration = sw.Elapsed;
            await PublishProgressAsync(libraryId, ScanPhase.Completed, cancellationToken, new() { Message = report.ErrorMessage });
            return report;
        }

        await ProcessMissingSeriesAsync(libraryId, walkedLibrary.MissingSeries, report, cancellationToken);
        await ProcessRecoveredSeriesAsync(walkedLibrary.RecoveredSeries, report, cancellationToken);
        var newlyCreatedSeries = await CreateNewSeriesAsync(libraryId, libraryPath, walkedLibrary.SeriesToCreate, report, cancellationToken);

        var allSeriesPaths = walkedLibrary.SeriesToVisit
            .Concat(newlyCreatedSeries.Select(s => s.Path))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await ReconcileMediaOwnershipAsync(libraryId, report, cancellationToken);

        var allObservedDirMtimes = await ProcessAllSeriesAsync(
            new ScanContext(libraryId, storedMtimes, thumbnailsDir, settings, report),
            allSeriesPaths,
            cancellationToken);

        await UpsertScannedDirMtimesAsync(allObservedDirMtimes, cancellationToken);

        await FinishScanAsync(library, libraryId, report, sw, cancellationToken);

        return report;
    }

    private async Task FinishScanAsync(
        Library library,
        string libraryId,
        LibraryScanReport report,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        // Sin esto la ficha decia "NUNCA ESCANEADA" por muchos escaneos que terminaran bien.
        library.LastScannedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        report.Duration = sw.Elapsed;
        report.Success = true;

        await PublishProgressAsync(libraryId, ScanPhase.Completed, cancellationToken, new()
        {
            CompletedSeries = (int)(report.CreatedSeries + report.UpdatedSeries),
            TotalSeries = (int)(report.CreatedSeries + report.UpdatedSeries),
            Message = $"Listo: {report.CreatedMedia} tomos nuevos, {report.UpdatedMedia} actualizados"
        });

        _logger.LogInformation(
            "Scan complete for library {LibraryId} in {ElapsedMs}ms. Created {CreatedSeries} series, {CreatedMedia} media. Updated {UpdatedSeries} series, {UpdatedMedia} media. Skipped {SkippedFiles} files.",
            libraryId,
            sw.ElapsedMilliseconds,
            report.CreatedSeries,
            report.CreatedMedia,
            report.UpdatedSeries,
            report.UpdatedMedia,
            report.SkippedFiles);
    }

    private async Task ReconcileMediaOwnershipAsync(
        string libraryId,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        var seriesOfLibrary = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId)
            .Select(s => new { s.Id, s.Path })
            .ToListAsync(cancellationToken);

        if (seriesOfLibrary.Count == 0) return;

        // De mas profunda a menos: el dueño de un fichero es la serie mas cercana por encima.
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in seriesOfLibrary)
        {
            owners.TryAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(s.Path)), s.Id);
        }

        var seriesIds = seriesOfLibrary.Select(s => s.Id).ToList();
        var media = await _dbContext.Media
            .Where(m => m.SeriesId != null && seriesIds.Contains(m.SeriesId))
            .ToListAsync(cancellationToken);

        if (media.Count == 0) return;

        var repointed = RepointMediaToOwners(media, owners);

        var removed = await RemoveDuplicateMediaRowsAsync(media, cancellationToken);

        if (repointed > 0 || removed > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            report.UpdatedMedia += (ulong)repointed;

            _logger.LogInformation(
                "Reconciled media for library {LibraryId}: {Repointed} repointed, {Removed} duplicate rows removed.",
                libraryId,
                repointed,
                removed);
        }

        foreach (var item in media)
        {
            _dbContext.Entry(item).State = EntityState.Detached;
        }
    }

    private async Task<int> RemoveDuplicateMediaRowsAsync(
        IReadOnlyList<Media> media,
        CancellationToken cancellationToken)
    {
        var duplicates = media
            .GroupBy(m => Path.GetFullPath(m.Path), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count == 0) return 0;

        var duplicateIds = duplicates.SelectMany(g => g.Select(m => m.Id)).ToList();
        var sessionCounts = await _dbContext.ReadingSessions
            .Where(rs => duplicateIds.Contains(rs.MediaId))
            .GroupBy(rs => rs.MediaId)
            .Select(g => new { MediaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MediaId, x => x.Count, cancellationToken);

        var removed = 0;
        foreach (var group in duplicates)
        {
            var survivor = group
                .OrderByDescending(m => sessionCounts.TryGetValue(m.Id, out var count) ? count : 0)
                .ThenBy(m => m.CreatedAt)
                .First();

            foreach (var loser in group.Where(m => !ReferenceEquals(m, survivor)))
            {
                // La miniatura lleva el id del tomo en el nombre, asi que la del descarte no es
                // la del superviviente y se puede borrar sin dejarle sin portada.
                TryDeleteThumbnail(loser.ThumbnailPath);
                _dbContext.Media.Remove(loser);
                removed++;
            }
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

    private static int RepointMediaToOwners(List<Media> media, Dictionary<string, string> owners)
    {
        var repointed = 0;
        foreach (var item in media)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(item.Path));
            if (string.IsNullOrEmpty(dir)) continue;

            var owner = FindOwningSeries(owners, dir);
            if (owner is null || string.Equals(owner, item.SeriesId, StringComparison.Ordinal)) continue;

            item.SeriesId = owner;
            repointed++;
        }
        return repointed;
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

        var seriesToMarkMissing = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId && missingPaths.Contains(s.Path))
            .ToListAsync(cancellationToken);

        var missingSeriesIds = seriesToMarkMissing.Select(s => s.Id).ToList();
        var mediaInMissingSeries = await _dbContext.Media
            .Where(m => m.SeriesId != null && missingSeriesIds.Contains(m.SeriesId))
            .ToListAsync(cancellationToken);

        foreach (var s in seriesToMarkMissing)
        {
            s.Status = FileStatus.Missing;
            report.UpdatedSeries++;
        }

        foreach (var m in mediaInMissingSeries)
        {
            m.Status = FileStatus.Missing;
            report.UpdatedMedia++;
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
        ScanContext context,
        IEnumerable<string> seriesPaths,
        CancellationToken cancellationToken)
    {
        var libraryId = context.LibraryId;
        var distinctPaths = seriesPaths.Distinct(StringComparer.Ordinal).ToList();
        var seriesEntities = await _dbContext.Series
            .Where(s => s.LibraryId == libraryId && distinctPaths.Contains(s.Path))
            .ToListAsync(cancellationToken);

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

        var walkedSeries = _directoryScanner.WalkSeries(series.Path, storedMtimes, existingMedia, allSeriesPaths);

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
        await CreateNewMediaAsync(series.Id, mediaToCreate, thumbnailsDir, settings, report, progress, cancellationToken);
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

        var bySource = conversions.ToDictionary(c => c.SourcePath, StringComparer.Ordinal);

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

    /// <summary>
    /// Crea los tomos nuevos de una serie por lotes.
    /// <para>
    /// Antes se analizaban los treinta y dos de golpe y se escribia una sola vez al final:
    /// las medidas de pagina de los treinta y dos —miles de filas— se quedaban en el
    /// rastreador de cambios hasta la ultima, y la ficha no tenia nada que enseñar mientras
    /// tanto. Un lote por vuelta acota esa memoria y deja avance visible tomo a tomo.
    /// </para>
    /// </summary>
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

        // Se anuncia el total antes de tocar el disco: el cliente puede dibujar los huecos
        // de los tomos que vienen en cuanto empieza, no cuando ya estan hechos.
        await PublishProgressAsync(progress.LibraryId, ScanPhase.ProcessingMedia, cancellationToken, new() { CompletedSeries = progress.CompletedSeries, TotalSeries = progress.TotalSeries, CurrentSeries = progress.SeriesName, TotalMedia = mediaToCreate.Count, CurrentSeriesId = progress.SeriesId });

        var done = 0;

        foreach (var chunk in mediaToCreate.Chunk(AnalysisParallelism))
        {
            var prepared = await PrepareMediaAsync(
                chunk.Select(path => (Ulid.NewUlid().ToString(), path)).ToList(),
                thumbnailsDir,
                settings.Analysis,
                cancellationToken);

            string? lastName = null;

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

                lastName = newMedia.Name;
                done++;
            }

            // Guardar aqui es lo que suelta el lote: hasta que no se escribe, el rastreador
            // sigue sujetando cada fila de pagina del lote entero.
            await _dbContext.SaveChangesAsync(cancellationToken);

            await PublishProgressAsync(progress.LibraryId, ScanPhase.ProcessingMedia, cancellationToken, new() { CompletedSeries = progress.CompletedSeries, TotalSeries = progress.TotalSeries, CurrentSeries = progress.SeriesName, CompletedMedia = done, TotalMedia = mediaToCreate.Count, CurrentMedia = lastName, CurrentSeriesId = progress.SeriesId });
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

        var visitedIds = existingOnDisk.Select(m => m.Id).ToList();
        var pagesByMedia = (await _dbContext.MediaPages
                .Where(mp => visitedIds.Contains(mp.MediaId))
                .ToListAsync(cancellationToken))
            .GroupBy(mp => mp.MediaId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

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

            if (pagesByMedia.TryGetValue(m.Id, out var staleDimensions))
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

    /// <summary>
    /// Caché de mtimes por carpeta. Se compara byte a byte, igual que la clave primaria de la
    /// tabla y que el propio sistema de ficheros: en Linux "parte 2" y "Parte 2" son carpetas
    /// distintas y la tabla tiene una fila para cada una. Metiéndolas en un diccionario que
    /// ignoraba mayusculas, la segunda reventaba el escaneo con ArgumentException.
    /// <para>
    /// De paso se van las filas cuya carpeta ya no esta en el disco. Nadie las borraba, asi
    /// que cada carpeta renombrada o eliminada dejaba la suya en la tabla para siempre.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, long>> LoadDirectoryMtimeCacheAsync(
        string libraryPath,
        CancellationToken cancellationToken)
    {
        var stored = await _dbContext.ScannedDirectories
            .Where(sd => sd.Path.StartsWith(libraryPath))
            .ToListAsync(cancellationToken);

        var cache = new Dictionary<string, long>(StringComparer.Ordinal);
        var stale = new List<ScannedDirectory>();

        foreach (var entry in stored)
        {
            if (Directory.Exists(entry.Path))
            {
                cache[entry.Path] = entry.LastMTime;
            }
            else
            {
                stale.Add(entry);
            }
        }

        if (stale.Count > 0)
        {
            _dbContext.ScannedDirectories.RemoveRange(stale);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Dropped {Count} cached directory entries with no folder on disk under {Path}.",
                stale.Count,
                libraryPath);
        }

        return cache;
    }

    private async Task UpsertScannedDirMtimesAsync(Dictionary<string, long> allObservedDirMtimes, CancellationToken cancellationToken)
    {
        if (allObservedDirMtimes.Count == 0) return;

        var observedPaths = allObservedDirMtimes.Keys.ToList();
        var existingScannedDirs = await _dbContext.ScannedDirectories
            .Where(sd => observedPaths.Contains(sd.Path))
            .ToDictionaryAsync(sd => sd.Path, sd => sd, StringComparer.Ordinal, cancellationToken);

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
