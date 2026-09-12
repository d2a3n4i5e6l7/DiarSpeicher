using System.Threading.Channels;
using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Infrastructure.Background;

public interface IScannerQueue
{
    ValueTask QueueScanAsync(ScanRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ScanRequest> ReadRequestsAsync(CancellationToken cancellationToken = default);
}

public class ScannerQueue : IScannerQueue
{
    private readonly Channel<ScanRequest> _channel;
    private readonly IScanProgressHub _hub;

    public ScannerQueue(IScanProgressHub hub, int capacity = 100)
    {
        _hub = hub;
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<ScanRequest>(options);
    }

    public ValueTask QueueScanAsync(ScanRequest request, CancellationToken cancellationToken = default)
    {
        _hub.MarkQueued(request.LibraryId);

        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<ScanRequest> ReadRequestsAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
