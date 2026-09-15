using Microsoft.Extensions.Hosting;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Construye el indice de nombres de carpeta al arrancar, en segundo plano.
/// <para>
/// Se espera un poco antes: el arranque ya compite por disco con la migracion y el primer
/// escaneo, y este repaso no tiene prisa porque la navegacion del explorador va en vivo.
/// </para>
/// </summary>
public sealed class FolderIndexStartupService : BackgroundService
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

    private readonly IFolderIndex _index;
    private readonly ILogger<FolderIndexStartupService> _logger;

    public FolderIndexStartupService(IFolderIndex index, ILogger<FolderIndexStartupService> logger)
    {
        _index = index;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_index.TryStartRefresh())
        {
            _logger.LogInformation("El indice de carpetas ya se estaba repasando");
        }
    }
}
