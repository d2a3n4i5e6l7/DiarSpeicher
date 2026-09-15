using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IScannerQueue _queue;
    private readonly StorageOptions _storage;
    private readonly ILogger<ScanRecoveryService> _logger;

    public ScanRecoveryService(
        IServiceScopeFactory scopeFactory,
        IScannerQueue queue,
        ILogger<ScanRecoveryService> logger,
        IOptions<StorageOptions>? storageOptions = null)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _storage = storageOptions?.Value ?? new StorageOptions();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Delay, stoppingToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();

            await PurgeOrphanMediaAsync(db, stoppingToken);
            await PurgeOrphanLibraryConfigsAsync(db, stoppingToken);
            await PurgeUnclaimedScannedDirectoriesAsync(db, stoppingToken);
            await PurgeUnreferencedThumbnailsAsync(db, stoppingToken);

            var libraries = await db.Libraries
                .Select(l => new { l.Id, l.Name })
                .ToListAsync(stoppingToken);

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
    private async Task PurgeOrphanMediaAsync(DiarSpeicherDbContext db, CancellationToken ct)
    {
        var orphans = await db.Media.Where(m => m.SeriesId == null).ToListAsync(ct);
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

        db.Media.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);

        _logger.LogWarning("Limpiados {Count} tomos sin serie", orphans.Count);
    }

    /// <summary>
    /// Configuraciones que ya no describen a nadie. La clave ajena va de Libraries hacia
    /// LibraryConfigs, asi que la cascada nunca actua en este sentido y cada biblioteca
    /// borrada antes de que esto existiera dejo la suya atras.
    /// </summary>
    private async Task PurgeOrphanLibraryConfigsAsync(DiarSpeicherDbContext db, CancellationToken ct)
    {
        var usedConfigIds = await db.Libraries.Select(l => l.ConfigId).Distinct().ToListAsync(ct);
        var orphans = await db.LibraryConfigs
            .Where(c => !usedConfigIds.Contains(c.Id))
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        db.LibraryConfigs.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);

        _logger.LogWarning("Limpiadas {Count} configuraciones sin biblioteca", orphans.Count);
    }

    /// <summary>
    /// Filas de la cache de mtimes que no caen bajo ninguna biblioteca viva. Un escaneo solo
    /// repasa las de su propia ruta, asi que las de una biblioteca borrada no las visita nadie.
    /// </summary>
    private async Task PurgeUnclaimedScannedDirectoriesAsync(DiarSpeicherDbContext db, CancellationToken ct)
    {
        var libraryPaths = await db.Libraries.Select(l => l.Path).ToListAsync(ct);
        var roots = libraryPaths
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .ToList();

        var cached = await db.ScannedDirectories.ToListAsync(ct);
        var unclaimed = cached
            .Where(entry => !roots.Any(root =>
                string.Equals(entry.Path, root, StringComparison.Ordinal)
                || entry.Path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();

        if (unclaimed.Count == 0) return;

        db.ScannedDirectories.RemoveRange(unclaimed);
        await db.SaveChangesAsync(ct);

        _logger.LogWarning("Limpiadas {Count} carpetas cacheadas sin biblioteca", unclaimed.Count);
    }

    /// <summary>
    /// Miniaturas que ya no reclama ninguna fila. Se borran desde varios sitios y basta con
    /// que uno se olvide para que el fichero sobreviva al tomo: el disco es la unica fuente
    /// fiable de lo que hay, y la base la de lo que se usa.
    /// </summary>
    private async Task PurgeUnreferencedThumbnailsAsync(DiarSpeicherDbContext db, CancellationToken ct)
    {
        var directory = _storage.ResolveThumbnailsPath();
        if (!Directory.Exists(directory)) return;

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in await db.Media.Where(m => m.ThumbnailPath != null).Select(m => m.ThumbnailPath!).ToListAsync(ct))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        foreach (var path in await db.Series.Where(s => s.ThumbnailPath != null).Select(s => s.ThumbnailPath!).ToListAsync(ct))
        {
            referenced.Add(Path.GetFullPath(path));
        }

        foreach (var path in await db.Libraries.Where(l => l.ThumbnailPath != null).Select(l => l.ThumbnailPath!).ToListAsync(ct))
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
