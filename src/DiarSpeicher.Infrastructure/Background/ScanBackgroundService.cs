using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Background;

public class ScanBackgroundService : BackgroundService
{
    private readonly IScannerQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanBackgroundService> _logger;

    public ScanBackgroundService(
        IScannerQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ScanBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScanBackgroundService worker started");

        try
        {
            await foreach (var libraryId in _queue.ReadRequestsAsync(stoppingToken).Select(r => r.LibraryId))
            {
                _logger.LogInformation("Processing scan request for library: {LibraryId}", libraryId);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scannerService = scope.ServiceProvider.GetRequiredService<ILibraryScannerService>();
                    var report = await scannerService.ScanLibraryAsync(libraryId, stoppingToken);

                    if (report.Success)
                    {
                        _logger.LogInformation(
                            "Scan finished successfully for library {LibraryId} in {ElapsedMs}ms",
                            libraryId,
                            report.Duration.TotalMilliseconds);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Scan finished with failure for library {LibraryId}: {Error}",
                            libraryId,
                            report.ErrorMessage);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Unhandled exception while scanning library {LibraryId}", libraryId);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "ScanBackgroundService was cancelled");
        }

        _logger.LogInformation("ScanBackgroundService worker stopped");
    }
}
