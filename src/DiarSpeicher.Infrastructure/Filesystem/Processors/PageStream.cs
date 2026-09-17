namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public sealed class PageStream : Stream
{
    private readonly Stream _inner;
    private readonly IAsyncDisposable? _owned;

    private PageStream(Stream inner, IAsyncDisposable? owned)
    {
        _inner = inner;
        _owned = owned;
    }

    public static PageStream Owning(Stream inner, IAsyncDisposable owner) => new(inner, owner);

    public static PageStream Detached(Stream inner) => new(inner, null);

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush() => _inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        _inner.Dispose();

        if (_owned is not null)
        {
            _owned.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();

        if (_owned is not null)
        {
            await _owned.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
