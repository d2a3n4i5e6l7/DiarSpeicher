using Dapper;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Background;

public sealed class ScanRecoveryService : BackgroundService
{
    /// <summary>
    /// Margen para que la base de datos termine de migrar y el disco deje de pelearse
    /// consigo mismo en el arranque.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Una miniatura se escribe en disco antes de que se guarde la fila del tomo. Sin este
    /// margen, un repaso que coincidiera con un escaneo en marcha borraria portadas recien
    /// creadas por no encontrar todavia quien las referencie.
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
            var libraries = (await conn.QueryAsync<(string Id, string Name)>(
                new CommandDefinition(libSql, cancellationToken: stoppingToken))).ToList();

            foreach (var library in libraries)
            {
                await _queue.QueueScanAsync(new ScanRequest(library.Id), stoppingToken);
            }

            _logger.LogInformation("Reencoladas {Count} bibliotecas tras el arranque", libraries.Count);
        }
        catch (OperationCanceledException)
        {
            // El proceso se esta parando.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "No se pudieron reencolar las bibliotecas al arrancar");
        }
    }

    /// <summary>
    /// Tomos sin serie. No deberian existir: el escaner siempre asigna una, y la subida no
    /// crea filas. Aparecen cuando algo borro la serie y la relacion, declarada SetNull,
    /// dejo el tomo colgando. Son inalcanzables desde su biblioteca pero salen en "anadido
    /// reciente", asi que se limpian al arrancar.
    /// </summary>
    private async Task PurgeOrphanMediaAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        const string selectSql = """SELECT "Id", "ThumbnailPath" FROM "Media" WHERE "SeriesId" IS NULL;""";
        var orphans = (await conn.QueryAsync<(string Id, string? ThumbnailPath)>(
            new CommandDefinition(selectSql, cancellationToken: ct))).ToList();

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
        await conn.ExecuteAsync(new CommandDefinition(deleteSql, cancellationToken: ct));

        _logger.LogWarning("Limpiados {Count} tomos sin serie", orphans.Count);
    }

    /// <summary>
    /// Configuraciones que ya no describen a nadie. La clave ajena va de Libraries hacia
    /// LibraryConfigs, asi que la cascada nunca actua en este sentido y cada biblioteca
    /// borrada antes de que esto existiera dejo la suya atras.
    /// </summary>
    private async Task PurgeOrphanLibraryConfigsAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        const string sql = """
            DELETE FROM "LibraryConfigs"
            WHERE "Id" NOT IN (
                SELECT DISTINCT "ConfigId" FROM "Libraries" WHERE "ConfigId" IS NOT NULL
            );
            """;

        var count = await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
        if (count > 0)
        {
            _logger.LogWarning("Limpiadas {Count} configuraciones sin biblioteca", count);
        }
    }

    /// <summary>
    /// Filas de la cache de mtimes que no caen bajo ninguna biblioteca viva. Un escaneo solo
    /// repasa las de su propia ruta, asi que las de una biblioteca borrada no las visita nadie.
    /// </summary>
    private async Task PurgeUnclaimedScannedDirectoriesAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        const string libSql = """SELECT "Path" FROM "Libraries";""";
        var libraryPaths = await conn.QueryAsync<string>(new CommandDefinition(libSql, cancellationToken: ct));
        var roots = libraryPaths
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .ToList();

        const string dirSql = """SELECT "Path" FROM "ScannedDirectories";""";
        var cached = (await conn.QueryAsync<string>(new CommandDefinition(dirSql, cancellationToken: ct))).ToList();

        var unclaimedPaths = cached
            .Where(path => !roots.Any(root =>
                string.Equals(path, root, StringComparison.Ordinal)
                || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();

        if (unclaimedPaths.Count == 0) return;

        const string deleteSql = """DELETE FROM "ScannedDirectories" WHERE "Path" IN @Paths;""";
        await conn.ExecuteAsync(new CommandDefinition(deleteSql, new { Paths = unclaimedPaths }, cancellationToken: ct));

        _logger.LogWarning("Limpiadas {Count} carpetas cacheadas sin biblioteca", unclaimedPaths.Count);
    }

    /// <summary>
    /// Miniaturas que ya no reclama ninguna fila. Se borran desde varios sitios y basta con
    /// que uno se olvide para que el fichero sobreviva al tomo: el disco es la unica fuente
    /// fiable de lo que hay, y la base la de lo que se usa.
    /// </summary>
    private async Task PurgeUnreferencedThumbnailsAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        var directory = _storage.ResolveThumbnailsPath();
        if (!Directory.Exists(directory)) return;

        var referenced = new HashSet<string>(StringComparer.Ordinal);

        const string mediaSql = """SELECT "ThumbnailPath" FROM "Media" WHERE "ThumbnailPath" IS NOT NULL;""";
        foreach (var path in await conn.QueryAsync<string>(new CommandDefinition(mediaSql, cancellationToken: ct)))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        const string seriesSql = """SELECT "ThumbnailPath" FROM "Series" WHERE "ThumbnailPath" IS NOT NULL;""";
        foreach (var path in await conn.QueryAsync<string>(new CommandDefinition(seriesSql, cancellationToken: ct)))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        const string libSql = """SELECT "ThumbnailPath" FROM "Libraries" WHERE "ThumbnailPath" IS NOT NULL;""";
        foreach (var path in await conn.QueryAsync<string>(new CommandDefinition(libSql, cancellationToken: ct)))
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
