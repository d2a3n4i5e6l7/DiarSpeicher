using System.Data;
using System.Diagnostics;
using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly IThumbnailService _thumbnailService;
    private readonly IArchiveConversionService _archiveConverter;
    private readonly StorageOptions _storage;
    private readonly ILogger<LibraryScannerService> _logger;
    private readonly IScanProgressPublisher? _progressPublisher;

    /// <summary>
    /// Archive decompression and hashing are CPU-bound, so the analysis phase is spread
    /// across cores. The database phase stays single-threaded and executes via lightweight Dapper batches.
    /// </summary>
    private static readonly int AnalysisParallelism = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    /// Tomos por vuelta al crear. Fija el techo de libros descomprimidos a la vez, que es
    /// donde esta el pico de memoria del escaneo, y tambien el grano con el que los tomos
    /// nuevos llegan a la ficha.
    /// </summary>
    private const int MediaBatchSize = 2;

    private sealed record PreparedMedia(string MediaId, string Path, ProcessedBook Analysis, string? ThumbnailPath);

    /// <summary>
    /// Lo que hace falta para situar un tomo dentro del escaneo completo. Viaja hacia abajo
    /// porque quien conoce el avance por tomos es el bucle de creacion, y quien conoce el
    /// avance por series esta tres niveles mas arriba.
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
        ISqliteConnectionFactory connectionFactory,
        IDirectoryScanner directoryScanner,
        ICompositeBookProcessor bookProcessor,
        IThumbnailService thumbnailService,
        IArchiveConversionService archiveConverter,
        ILogger<LibraryScannerService> logger,
        LibraryScannerOptions? options = null)
    {
        _connectionFactory = connectionFactory;
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
        public IReadOnlyList<string> PendingMedia { get; init; } = [];
        public string? CurrentFile { get; init; }
        public IReadOnlyList<string> ReadingFiles { get; init; } = [];
        public IReadOnlyList<DiarSpeicherMediaDto> CreatedMedia { get; init; } = [];
        public IReadOnlyList<DiarSpeicherSeriesDto> CreatedSeries { get; init; } = [];
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
            PendingMedia = d.PendingMedia,
            CurrentFile = d.CurrentFile,
            ReadingFiles = d.ReadingFiles,
            CreatedMedia = d.CreatedMedia,
            CreatedSeries = d.CreatedSeries,
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
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT 
                l."Id", l."Name", l."Description", l."Path", l."Status", l."Emoji", 
                l."ThumbnailPath", l."ThumbnailMeta", l."CreatedAt", l."UpdatedAt", l."LastScannedAt", l."ConfigId",
                c."Id", c."ConvertRarToZip", c."HardDeleteConversions", c."DefaultReadingDir", c."DefaultReadingMode",
                c."GenerateFileHashes", c."GenerateKoreaderHashes", c."ProcessMetadata", c."Watch",
                c."LibraryPattern", c."DefaultLibraryViewMode", c."HideSeriesView", c."LibraryType",
                c."ThumbnailWidth", c."ThumbnailHeight", c."IgnoreRules"
            FROM "Libraries" l
            LEFT JOIN "LibraryConfigs" c ON c."Id" = l."ConfigId"
            WHERE l."Id" = @LibraryId
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<Library, LibraryConfig?, Library>(
            new CommandDefinition(sql, new { LibraryId = libraryId }, cancellationToken: cancellationToken),
            (lib, cfg) =>
            {
                if (cfg is not null) lib.Config = cfg;
                return lib;
            },
            splitOn: "Id");
        var library = rows.FirstOrDefault();

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
            const string updateMissingSql = @"UPDATE ""Libraries"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" = @Id;";
            await conn.ExecuteAsync(new CommandDefinition(
                updateMissingSql,
                new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, Id = library.Id },
                cancellationToken: cancellationToken));

            report.Success = false;
            report.ErrorMessage = $"Library directory {libraryPath} does not exist";
            report.Duration = sw.Elapsed;
            await PublishProgressAsync(libraryId, ScanPhase.Failed, cancellationToken, new() { Message = report.ErrorMessage });
            return null;
        }

        if (library.Status.IsRecoveredIfPresent())
        {
            library.Status = FileStatus.Ready;
            const string updateReadySql = @"UPDATE ""Libraries"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" = @Id;";
            await conn.ExecuteAsync(new CommandDefinition(
                updateReadySql,
                new { Status = nameof(FileStatus.Ready), UpdatedAt = DateTimeOffset.UtcNow, Id = library.Id },
                cancellationToken: cancellationToken));
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

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string existingSeriesSql = @"SELECT ""Id"", ""Path"", ""Status"" FROM ""Series"" WHERE ""LibraryId"" = @LibraryId;";
        var existingSeries = (await conn.QueryAsync<ExistingSeriesInfo>(
            new CommandDefinition(existingSeriesSql, new { LibraryId = libraryId }, cancellationToken: cancellationToken)
        )).AsList();

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
        var now = DateTimeOffset.UtcNow;
        library.LastScannedAt = now;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string sql = @"UPDATE ""Libraries"" SET ""LastScannedAt"" = @LastScannedAt WHERE ""Id"" = @Id;";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { LastScannedAt = now, Id = libraryId }, cancellationToken: cancellationToken));

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

    private sealed record MediaOwnershipItem(string Id, string Path, string? SeriesId, DateTimeOffset CreatedAt, string? ThumbnailPath);

    private async Task ReconcileMediaOwnershipAsync(
        string libraryId,
        LibraryScanReport report,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string seriesSql = @"SELECT ""Id"", ""Path"" FROM ""Series"" WHERE ""LibraryId"" = @LibraryId;";
        var seriesOfLibrary = (await conn.QueryAsync<(string Id, string Path)>(
            new CommandDefinition(seriesSql, new { LibraryId = libraryId }, cancellationToken: cancellationToken)
        )).AsList();

        if (seriesOfLibrary.Count == 0) return;

        // De mas profunda a menos: el dueño de un fichero es la serie mas cercana por encima.
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in seriesOfLibrary)
        {
            owners.TryAdd(Path.TrimEndingDirectorySeparator(Path.GetFullPath(s.Path)), s.Id);
        }

        var seriesIds = seriesOfLibrary.Select(s => s.Id).ToList();
        const string mediaSql = @"SELECT ""Id"", ""Path"", ""SeriesId"", ""CreatedAt"", ""ThumbnailPath"" FROM ""Media"" WHERE ""SeriesId"" IN @SeriesIds;";
        var mediaList = (await conn.QueryAsync<MediaOwnershipItem>(
            new CommandDefinition(mediaSql, new { SeriesIds = seriesIds }, cancellationToken: cancellationToken)
        )).AsList();

        if (mediaList.Count == 0) return;

        var toRepoint = new List<(string MediaId, string NewSeriesId)>();
        foreach (var item in mediaList)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(item.Path));
            if (string.IsNullOrEmpty(dir)) continue;

            var owner = FindOwningSeries(owners, dir);
            if (owner is null || string.Equals(owner, item.SeriesId, StringComparison.Ordinal)) continue;

            toRepoint.Add((item.Id, owner));
        }

        var removed = await RemoveDuplicateMediaRowsAsync(conn, mediaList, cancellationToken);

        if (toRepoint.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            const string updateSql = @"UPDATE ""Media"" SET ""SeriesId"" = @NewSeriesId, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" = @MediaId;";
            var now = DateTimeOffset.UtcNow;
            await conn.ExecuteAsync(new CommandDefinition(
                updateSql,
                toRepoint.Select(x => new { NewSeriesId = x.NewSeriesId, UpdatedAt = now, MediaId = x.MediaId }),
                tx,
                cancellationToken: cancellationToken));
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
        const string sessionsSql = @"SELECT ""MediaId"", COUNT(*) AS ""Count"" FROM ""ReadingSessions"" WHERE ""MediaId"" IN @DuplicateIds GROUP BY ""MediaId"";";
        var sessionCounts = (await conn.QueryAsync<(string MediaId, int Count)>(
            new CommandDefinition(sessionsSql, new { DuplicateIds = duplicateIds }, cancellationToken: cancellationToken)
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
                // La miniatura lleva el id del tomo en el nombre, asi que la del descarte no es
                // la del superviviente y se puede borrar sin dejarle sin portada.
                TryDeleteThumbnail(loser.ThumbnailPath);
                toDeleteIds.Add(loser.Id);
                removed++;
            }
        }

        if (toDeleteIds.Count > 0)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
            const string deleteSql = @"DELETE FROM ""Media"" WHERE ""Id"" IN @Ids;";
            await conn.ExecuteAsync(new CommandDefinition(deleteSql, new { Ids = toDeleteIds }, tx, cancellationToken: cancellationToken));
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

        const string selectSeriesSql = @"SELECT ""Id"" FROM ""Series"" WHERE ""LibraryId"" = @LibraryId AND ""Path"" IN @Paths;";
        var missingSeriesIds = (await conn.QueryAsync<string>(
            new CommandDefinition(selectSeriesSql, new { LibraryId = libraryId, Paths = missingPaths }, tx, cancellationToken: cancellationToken)
        )).AsList();

        if (missingSeriesIds.Count > 0)
        {
            const string updateSeriesSql = @"UPDATE ""Series"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" IN @Ids;";
            var updatedSeriesCount = await conn.ExecuteAsync(new CommandDefinition(
                updateSeriesSql,
                new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, Ids = missingSeriesIds },
                tx,
                cancellationToken: cancellationToken));
            report.UpdatedSeries += (ulong)updatedSeriesCount;

            const string updateMediaSql = @"UPDATE ""Media"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""SeriesId"" IN @SeriesIds AND ""Status"" != @Status;";
            var updatedMediaCount = await conn.ExecuteAsync(new CommandDefinition(
                updateMediaSql,
                new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, SeriesIds = missingSeriesIds },
                tx,
                cancellationToken: cancellationToken));
            report.UpdatedMedia += (ulong)updatedMediaCount;
        }

        await tx.CommitAsync(cancellationToken);
    }

    private async Task ProcessRecoveredSeriesAsync(List<string> recoveredIds, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (recoveredIds.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string updateSql = @"UPDATE ""Series"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" IN @Ids;";
        var updatedCount = await conn.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { Status = nameof(FileStatus.Ready), UpdatedAt = DateTimeOffset.UtcNow, Ids = recoveredIds },
            cancellationToken: cancellationToken));
        report.UpdatedSeries += (ulong)updatedCount;
    }

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

            var seriesRows = newlyCreated.Select(x => new
            {
                x.Id, x.Name, x.Path, x.LibraryId,
                Status = x.Status.ToString(),
                x.CreatedAt, x.UpdatedAt
            }).ToList();
            await conn.ExecuteAsync(new CommandDefinition(insertSql, seriesRows, tx, cancellationToken: cancellationToken));
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
            WHERE "LibraryId" = @LibraryId AND "Path" IN @Paths;
            """;
        var seriesEntities = (await conn.QueryAsync<Series>(
            new CommandDefinition(sql, new { LibraryId = libraryId, Paths = distinctPaths }, cancellationToken: cancellationToken)
        )).AsList();

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

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string mediaSql = @"SELECT ""Id"", ""Path"", ""Status"", ""ModifiedAt"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId;";
        var existingMedia = (await conn.QueryAsync<ExistingMediaInfo>(
            new CommandDefinition(mediaSql, new { SeriesId = series.Id }, cancellationToken: cancellationToken)
        )).AsList();

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
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        const string selectSql = @"SELECT ""Id"", ""Path"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId AND ""Path"" IN @SourcePaths;";
        var affected = (await conn.QueryAsync<(string Id, string Path)>(
            new CommandDefinition(selectSql, new { SeriesId = seriesId, SourcePaths = sourcePaths }, cancellationToken: cancellationToken)
        )).AsList();

        if (affected.Count == 0) return;

        var bySource = conversions.ToDictionary(c => c.SourcePath, StringComparer.Ordinal);
        var toUpdate = new List<object>();
        var now = DateTimeOffset.UtcNow;

        foreach (var item in affected)
        {
            if (!bySource.TryGetValue(item.Path, out var conversion)) continue;

            toUpdate.Add(new
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
            await conn.ExecuteAsync(new CommandDefinition(updateSql, toUpdate, tx, cancellationToken: cancellationToken));
            await tx.CommitAsync(cancellationToken);
        }
    }

    private async Task MarkMissingMediaAsync(string seriesId, List<string> missingPaths, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (missingPaths.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string updateSql = @"UPDATE ""Media"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""SeriesId"" = @SeriesId AND ""Path"" IN @Paths AND ""Status"" != @Status;";
        var count = await conn.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, SeriesId = seriesId, Paths = missingPaths },
            cancellationToken: cancellationToken));
        report.UpdatedMedia += (ulong)count;
    }

    private async Task MarkRecoveredMediaAsync(List<string> recoveredIds, LibraryScanReport report, CancellationToken cancellationToken)
    {
        if (recoveredIds.Count == 0) return;

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string updateSql = @"UPDATE ""Media"" SET ""Status"" = @Status, ""UpdatedAt"" = @UpdatedAt WHERE ""Id"" IN @Ids AND ""Status"" != @Status;";
        var count = await conn.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { Status = nameof(FileStatus.Ready), UpdatedAt = DateTimeOffset.UtcNow, Ids = recoveredIds },
            cancellationToken: cancellationToken));
        report.UpdatedMedia += (ulong)count;
    }

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

    /// <summary>
    /// Crea los tomos nuevos de una serie, uno a uno.
    /// Ejecuta inserciones masivas directamente via Dapper y SQLite sin ChangeTracker de EF Core,
    /// eliminando cualquier retención acumulativa de memoria y garantizando O(1) de asignación.
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
        var pendingNames = mediaToCreate.ToList();

        await PublishProgressAsync(progress.LibraryId, ScanPhase.ProcessingMedia, cancellationToken, new() { CompletedSeries = progress.CompletedSeries, TotalSeries = progress.TotalSeries, CurrentSeries = progress.SeriesName, TotalMedia = mediaToCreate.Count, PendingMedia = pendingNames, CurrentSeriesId = progress.SeriesId });

        var done = 0;
        var announced = new List<DiarSpeicherMediaDto>();

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        foreach (var chunk in mediaToCreate.Chunk(MediaBatchSize))
        {
            // El aviso va antes de analizar, no despues: el analisis es lo que tarda, y sin
            // esto el hueco del tomo se enciende justo cuando ya ha dejado de leerse.
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
        var mediaRows = batchCreated.Select(m => new
        {
            m.Id, m.Name, m.SortName, m.Size, m.Extension, m.Pages, m.Path,
            Status = m.Status.ToString(),
            m.Hash, m.KoreaderHash, m.ThumbnailPath, m.ThumbnailMeta,
            m.CreatedAt, m.UpdatedAt, m.ModifiedAt, m.DeletedAt, m.SeriesId
        }).ToList();
        await conn.ExecuteAsync(new CommandDefinition(insertMediaSql, mediaRows, tx, cancellationToken: cancellationToken));

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
            await conn.ExecuteAsync(new CommandDefinition(insertMetaSql, metadataToInsert, tx, cancellationToken: cancellationToken));
        }

        if (pagesToInsert.Count > 0)
        {
            const string insertPagesSql = """
                INSERT INTO "MediaPages" ("MediaId", "Number", "FileName", "MediaType", "Width", "Height", "SizeBytes")
                VALUES (@MediaId, @Number, @FileName, @MediaType, @Width, @Height, @SizeBytes);
                """;
            await conn.ExecuteAsync(new CommandDefinition(insertPagesSql, pagesToInsert, tx, cancellationToken: cancellationToken));
        }

        await tx.CommitAsync(cancellationToken);
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

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string selectSql = @"SELECT ""Id"", ""Path"" FROM ""Media"" WHERE ""SeriesId"" = @SeriesId AND ""Path"" IN @Paths;";
        var mediaToUpdate = (await conn.QueryAsync<(string Id, string Path)>(
            new CommandDefinition(selectSql, new { SeriesId = seriesId, Paths = toVisitPaths }, cancellationToken: cancellationToken)
        )).AsList();

        var existingOnDisk = mediaToUpdate.Where(m => File.Exists(m.Path)).ToList();
        if (existingOnDisk.Count == 0) return;

        var prepared = await PrepareMediaAsync(
            existingOnDisk.Select(m => (m.Id, m.Path)).ToList(),
            thumbnailsDir,
            settings.Analysis,
            cancellationToken);

        var byId = prepared.ToDictionary(p => p.MediaId, StringComparer.Ordinal);
        var visitedIds = existingOnDisk.Select(m => m.Id).ToList();

        var mediaUpdateList = new List<object>();
        var newMediaPages = new List<MediaPage>();
        var now = DateTimeOffset.UtcNow;

        foreach (var m in existingOnDisk)
        {
            if (!byId.TryGetValue(m.Id, out var result)) continue;

            var fileInfo = new FileInfo(m.Path);
            if (!fileInfo.Exists) continue;

            var analyzed = result.Analysis;
            var thumbPath = result.ThumbnailPath;

            mediaUpdateList.Add(new
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
            await conn.ExecuteAsync(new CommandDefinition(updateMediaSql, mediaUpdateList, tx, cancellationToken: cancellationToken));

            const string deletePagesSql = @"DELETE FROM ""MediaPages"" WHERE ""MediaId"" IN @MediaIds;";
            await conn.ExecuteAsync(new CommandDefinition(deletePagesSql, new { MediaIds = visitedIds }, tx, cancellationToken: cancellationToken));

            if (newMediaPages.Count > 0)
            {
                const string insertPagesSql = """
                    INSERT INTO "MediaPages" ("MediaId", "Number", "FileName", "MediaType", "Width", "Height", "SizeBytes")
                    VALUES (@MediaId, @Number, @FileName, @MediaType, @Width, @Height, @SizeBytes);
                    """;
                await conn.ExecuteAsync(new CommandDefinition(insertPagesSql, newMediaPages, tx, cancellationToken: cancellationToken));
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
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        const string selectSql = @"SELECT ""Path"", ""LastMTime"" FROM ""ScannedDirectories"" WHERE substr(""Path"", 1, @Len) = @Prefix;";
        var stored = (await conn.QueryAsync<ScannedDirectory>(
            new CommandDefinition(selectSql, new { Prefix = libraryPath, Len = libraryPath.Length }, cancellationToken: cancellationToken)
        )).AsList();

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
            const string deleteSql = @"DELETE FROM ""ScannedDirectories"" WHERE ""Path"" IN @Paths;";
            await conn.ExecuteAsync(new CommandDefinition(deleteSql, new { Paths = stalePaths }, cancellationToken: cancellationToken));

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
        await conn.ExecuteAsync(new CommandDefinition(upsertSql, parameters, tx, cancellationToken: cancellationToken));

        await tx.CommitAsync(cancellationToken);
    }
}
