using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Reencola las bibliotecas al arrancar, para que un corte de luz a mitad de un escaneo no
/// deje el indice a medias.
/// <para>
/// No hace falta una tabla de trabajos pendientes: un escaneo es idempotente y ya lleva su
/// propia cache incremental en <c>ScannedDirectories</c>, asi que reencolarlo todo cuesta
/// casi nada cuando no ha cambiado nada y termina lo que quedo a medias cuando si.
/// </para>
/// </summary>
public sealed class ScanRecoveryService : BackgroundService
{
    /// <summary>
    /// Margen para que la base de datos termine de migrar y el disco deje de pelearse
    /// consigo mismo en el arranque.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IScannerQueue _queue;
    private readonly ILogger<ScanRecoveryService> _logger;

    public ScanRecoveryService(
        IServiceScopeFactory scopeFactory,
        IScannerQueue queue,
        ILogger<ScanRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Delay, stoppingToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();

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
}
