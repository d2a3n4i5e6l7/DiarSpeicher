using Microsoft.Extensions.Hosting;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// It waits a little first: startup already competes for disk with the migration and the first
/// scan, and this pass is in no hurry because browsing in the explorer runs live.
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
