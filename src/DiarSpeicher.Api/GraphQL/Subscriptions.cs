using DiarSpeicher.Core.Filesystem;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;

namespace DiarSpeicher.Api.GraphQL;

public class Subscription
{
    /// <summary>
    /// Live progress for one scan. The topic is per job, so a client watching one library
    /// is not woken by scans of every other library.
    /// </summary>
    [Subscribe(With = nameof(SubscribeToJobProgressAsync))]
    public ScanProgressEvent JobProgress(
        string jobId,
        [EventMessage] ScanProgressEvent progress) => progress;

    public static ValueTask<ISourceStream<ScanProgressEvent>> SubscribeToJobProgressAsync(
        string jobId,
        ITopicEventReceiver receiver,
        CancellationToken ct) =>
        receiver.SubscribeAsync<ScanProgressEvent>(ScanProgressTopics.ForJob(jobId), ct);
}

public static class ScanProgressTopics
{
    public static string ForJob(string jobId) => $"jobProgress:{jobId}";
}

/// <summary>
/// Bridges the scanner, which knows nothing about GraphQL, to HotChocolate's in-memory
/// pub/sub. Publishing must never take a scan down, so failures are swallowed after logging.
/// </summary>
public class GraphQLScanProgressPublisher : IScanProgressPublisher
{
    private readonly ITopicEventSender _sender;
    private readonly ILogger<GraphQLScanProgressPublisher> _logger;

    public GraphQLScanProgressPublisher(
        ITopicEventSender sender,
        ILogger<GraphQLScanProgressPublisher> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default)
    {
        try
        {
            await _sender.SendAsync(ScanProgressTopics.ForJob(progress.JobId), progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not publish scan progress for job {JobId}", progress.JobId);
        }
    }
}
