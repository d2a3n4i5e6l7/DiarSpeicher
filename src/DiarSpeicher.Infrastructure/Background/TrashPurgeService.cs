using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Vacia la papelera cuando lo borrado cumple su plazo. Corre cada cinco minutos: la
/// caducidad es de una hora, asi que afinar mas solo gastaria vueltas al disco.
/// </summary>
public sealed class TrashPurgeService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ITrashService _trash;
    private readonly ILogger<TrashPurgeService> _logger;

    public TrashPurgeService(ITrashService trash, ILogger<TrashPurgeService> logger)
    {
        _trash = trash;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _trash.PurgeExpired();
            }
            catch (Exception e)
            {
                // Vaciar la papelera no puede tumbar el proceso: en la siguiente vuelta se reintenta.
                _logger.LogError(e, "Fallo el purgado de la papelera");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
