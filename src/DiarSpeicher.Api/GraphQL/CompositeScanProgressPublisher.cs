using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Api.GraphQL;

/// <summary>
/// El mismo progreso por los dos caminos: la suscripcion de GraphQL, que ya existia, y el
/// reparto en memoria que alimenta SSE. Publicar nunca puede tumbar un escaneo, asi que un
/// fallo de un destino no impide el otro.
/// </summary>
public sealed class CompositeScanProgressPublisher : IScanProgressPublisher
{
    private readonly IEnumerable<IScanProgressPublisher> _targets;
    private readonly ILogger<CompositeScanProgressPublisher> _logger;

    public CompositeScanProgressPublisher(
        IEnumerable<IScanProgressPublisher> targets,
        ILogger<CompositeScanProgressPublisher> logger)
    {
        _targets = targets;
        _logger = logger;
    }

    public async ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default)
    {
        foreach (var target in _targets)
        {
            try
            {
                await target.PublishAsync(progress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "No se pudo publicar el progreso en {Target}", target.GetType().Name);
            }
        }
    }
}
