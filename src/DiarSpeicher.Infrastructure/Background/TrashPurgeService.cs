using Microsoft.Extensions.Hosting;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Runs every five minutes: expiry is one hour, so a finer interval would only spin the disk.
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
                // Emptying the trash must not take the process down: the next round retries.
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
