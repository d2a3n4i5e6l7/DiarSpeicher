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

    public ScannerQueue(int capacity = 100)
    {
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
        return _channel.Writer.WriteAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<ScanRequest> ReadRequestsAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
