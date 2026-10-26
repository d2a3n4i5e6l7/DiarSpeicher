using Dapper;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Background;

public sealed class ScanRecoveryService : BackgroundService
{
    /// <summary>
    /// Gives the database time to finish migrating and the disk to stop fighting itself at
    /// startup.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A thumbnail is written to disk before the volume row is saved. Without this grace
    /// period, a pass that overlapped a running scan would delete freshly created covers for
    /// not yet finding anything that references them.
    /// </summary>
    private static readonly TimeSpan ThumbnailGrace = TimeSpan.FromMinutes(30);

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IScannerQueue _queue;
    private readonly StorageOptions _storage;
    private readonly ILogger<ScanRecoveryService> _logger;

    public ScanRecoveryService(
        ISqliteConnectionFactory connectionFactory,
        IScannerQueue queue,
        ILogger<ScanRecoveryService> logger,
        IOptions<StorageOptions>? storageOptions = null)
    {
        _connectionFactory = connectionFactory;
        _queue = queue;
        _storage = storageOptions?.Value ?? new StorageOptions();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Delay, stoppingToken);

            await using var conn = await _connectionFactory.CreateConnectionAsync(stoppingToken);

            await PurgeOrphanMediaAsync(conn, stoppingToken);
            await PurgeOrphanLibraryConfigsAsync(conn, stoppingToken);
            await PurgeUnclaimedScannedDirectoriesAsync(conn, stoppingToken);
            await PurgeUnreferencedThumbnailsAsync(conn, stoppingToken);

            const string libSql = """SELECT "Id", "Name" FROM "Libraries";""";
            var libraries = (await conn.QueryAsync<IdNameRow>(
                libSql)).ToList();

            foreach (var library in libraries)
            {
                await _queue.QueueScanAsync(new ScanRequest(library.Id), stoppingToken);
            }

            _logger.LogInformation("Reencoladas {Count} bibliotecas tras el arranque", libraries.Count);
        }
        catch (OperationCanceledException)
        {
            // The process is shutting down.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "No se pudieron reencolar las bibliotecas al arrancar");
        }
    }

    /// <summary>
    /// Volumes without a series should not exist: the scanner always assigns one and uploads
    /// create no rows. They appear when something deleted the series and the SetNull relation
    /// left the volume dangling. They are unreachable from their library but show up in
    /// "recently added", so they are cleaned at startup.
    /// </summary>
    private async Task PurgeOrphanMediaAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string selectSql = """SELECT "Id", "ThumbnailPath" FROM "Media" WHERE "SeriesId" IS NULL;""";
        var orphans = (await conn.QueryAsync<IdThumbnailRow>(
            selectSql)).ToList();

        if (orphans.Count == 0) return;

        foreach (var thumb in orphans.Select(m => m.ThumbnailPath))
        {
            if (string.IsNullOrWhiteSpace(thumb)) continue;

            try
            {
                File.Delete(thumb);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(e, "No se pudo borrar la miniatura huerfana {Path}", thumb);
            }
        }

        const string deleteSql = """DELETE FROM "Media" WHERE "SeriesId" IS NULL;""";
        await conn.ExecuteAsync(deleteSql);

        _logger.LogWarning("Limpiados {Count} tomos sin serie", orphans.Count);
    }

    /// <summary>
    /// The foreign key goes from Libraries to LibraryConfigs, so the cascade never acts in this
    /// direction and a deleted library leaves its configuration behind.
    /// </summary>
    private async Task PurgeOrphanLibraryConfigsAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            DELETE FROM "LibraryConfigs"
            WHERE "Id" NOT IN (
                SELECT DISTINCT "ConfigId" FROM "Libraries" WHERE "ConfigId" IS NOT NULL
            );
            """;

        var count = await conn.ExecuteAsync(sql);
        if (count > 0)
        {
            _logger.LogWarning("Limpiadas {Count} configuraciones sin biblioteca", count);
        }
    }

    /// <summary>
    /// Rows of the mtime cache that fall under no live library. A scan only revisits the rows
    /// under its own path, so those of a deleted library are never visited by anyone.
    /// </summary>
    private async Task PurgeUnclaimedScannedDirectoriesAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        const string libSql = """SELECT "Path" FROM "Libraries";""";
        var libraryPaths = await conn.QueryAsync<string>(libSql);
        var roots = libraryPaths
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .ToList();

        const string dirSql = """SELECT "Path" FROM "ScannedDirectories";""";
        var cached = (await conn.QueryAsync<string>(dirSql)).ToList();

        var unclaimedPaths = cached
            .Where(path => !roots.Any(root =>
                string.Equals(path, root, StringComparison.Ordinal)
                || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();

        if (unclaimedPaths.Count == 0) return;

        const string deleteSql = """DELETE FROM "ScannedDirectories" WHERE "Path" IN (SELECT value FROM json_each(@Paths));""";
        await conn.ExecuteAsync(deleteSql, new { Paths = SqlKeys.ToJson(unclaimedPaths) });

        _logger.LogWarning("Limpiadas {Count} carpetas cacheadas sin biblioteca", unclaimedPaths.Count);
    }

    /// <summary>
    /// Thumbnails no row claims anymore. They are deleted from several places and it takes only
    /// one forgetting for the file to outlive the volume: the disk is the only reliable source
    /// of what exists, and the database of what is used.
    /// </summary>
    private async Task PurgeUnreferencedThumbnailsAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        var directory = _storage.ResolveThumbnailsPath();
        if (!Directory.Exists(directory)) return;

        var referenced = new HashSet<string>(StringComparer.Ordinal);

        const string mediaSql = """SELECT "ThumbnailPath" FROM "Media" WHERE "ThumbnailPath" IS NOT NULL;""";
        foreach (var path in await conn.QueryAsync<string>(mediaSql))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        const string seriesSql = """SELECT "ThumbnailPath" FROM "Series" WHERE "ThumbnailPath" IS NOT NULL;""";
        foreach (var path in await conn.QueryAsync<string>(seriesSql))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        const string libSql = """SELECT "ThumbnailPath" FROM "Libraries" WHERE "ThumbnailPath" IS NOT NULL;""";
        foreach (var path in await conn.QueryAsync<string>(libSql))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        var cutoff = DateTime.UtcNow - ThumbnailGrace;
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var full = Path.GetFullPath(file);
            if (referenced.Contains(full)) continue;
            if (File.GetLastWriteTimeUtc(full) > cutoff) continue;

            try
            {
                File.Delete(full);
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(e, "No se pudo borrar la miniatura suelta {Path}", full);
            }
        }

        if (removed > 0)
        {
            _logger.LogWarning("Limpiadas {Count} miniaturas sin dueño", removed);
        }
    }
}
