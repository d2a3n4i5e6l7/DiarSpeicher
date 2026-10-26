using System.Diagnostics;
using System.IO;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using Microsoft.Extensions.Logging;
using DiarSpeicher.Infrastructure.Data.Extensions;

namespace DiarSpeicher.Infrastructure.Filesystem;

public partial class LibraryScannerService
{
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
                l."ThumbnailPath", l."ThumbnailMeta", l."CreatedAt", l."UpdatedAt", l."LastScannedAt", l."ConfigId"
            FROM "Libraries" l
            LEFT JOIN "LibraryConfigs" c ON c."Id" = l."ConfigId"
            WHERE l."Id" = @LibraryId
            LIMIT 1;
            """;

        var rows = await conn.QueryAsync<Library>(sql, new { LibraryId = libraryId }).WithConfigAsync(conn);
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
            await conn.ExecuteAsync(updateMissingSql, new { Status = nameof(FileStatus.Missing), UpdatedAt = DateTimeOffset.UtcNow, Id = library.Id });

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
            await conn.ExecuteAsync(updateReadySql, new { Status = nameof(FileStatus.Ready), UpdatedAt = DateTimeOffset.UtcNow, Id = library.Id });
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
            existingSeriesSql, new { LibraryId = libraryId }
        )).AsRowList();

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
        await conn.ExecuteAsync(sql, new { LastScannedAt = now, Id = libraryId });

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
}
