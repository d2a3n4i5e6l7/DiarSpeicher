using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DiarSpeicher.Infrastructure.Background;

public sealed record ScanSnapshot(
    ScanProgressEvent Progress,
    DateTimeOffset StartedAt,
    bool Finished)
{
    public bool Queued { get; init; }

    public TimeSpan Elapsed => DateTimeOffset.UtcNow - StartedAt;

    /// <summary>
    /// Null until at least one series is done: estimating with zero samples is making up a
    /// number.
    /// </summary>
    public TimeSpan? Eta
    {
        get
        {
            if (Finished || Queued || Progress.CompletedSeries <= 0 || Progress.TotalSeries <= 0) return null;

            var remaining = Progress.TotalSeries - Progress.CompletedSeries;
            if (remaining <= 0) return TimeSpan.Zero;

            var perSeries = Elapsed.TotalSeconds / Progress.CompletedSeries;

            return TimeSpan.FromSeconds(perSeries * remaining);
        }
    }
}

public interface IScanProgressHub
{
    ScanSnapshot? GetSnapshot(string libraryId);

    IReadOnlyCollection<ScanSnapshot> GetActive();

    /// <summary>
    /// The queue has a single reader, so queued scans wait for the previous one to finish.
    /// Without this there would be no way to tell "queued" from "never requested".
    /// </summary>
    void MarkQueued(string libraryId);

    IAsyncEnumerable<ScanSnapshot> SubscribeAsync(string libraryId, CancellationToken ct);
}

public sealed class NullScanProgressHub : IScanProgressHub
{
    public ScanSnapshot? GetSnapshot(string libraryId) => null;
    public IReadOnlyCollection<ScanSnapshot> GetActive() => [];
    public void MarkQueued(string libraryId) { }
    public async IAsyncEnumerable<ScanSnapshot> SubscribeAsync(string libraryId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>
/// Lives in memory and does not persist: if the process restarts mid-scan the scan dies too, so
/// there is nothing to recover.
/// </summary>
public sealed class ScanProgressHub : IScanProgressHub, IScanProgressPublisher
{
    private readonly ConcurrentDictionary<string, ScanSnapshot> _latest = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ScanSnapshot>>> _subscribers =
        new(StringComparer.Ordinal);

    public ScanSnapshot? GetSnapshot(string libraryId) =>
        _latest.TryGetValue(libraryId, out var snapshot) ? snapshot : null;

    public IReadOnlyCollection<ScanSnapshot> GetActive() =>
        _latest.Values.Where(s => !s.Finished).ToList();

    public void MarkQueued(string libraryId)
    {
        var queued = new ScanSnapshot(
            new ScanProgressEvent { JobId = libraryId, LibraryId = libraryId, Phase = ScanPhase.Started },
            DateTimeOffset.UtcNow,
            Finished: false)
        { Queued = true };

        _latest.AddOrUpdate(libraryId, queued, (_, previous) => previous.Finished ? queued : previous);
    }

    public ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default)
    {
        var finished = progress.Phase is ScanPhase.Completed or ScanPhase.Failed;

        var snapshot = _latest.AddOrUpdate(
            progress.LibraryId,
            _ => new ScanSnapshot(progress, DateTimeOffset.UtcNow, finished),
            // The start time is kept across events: it is the only basis for the estimate.
            (_, previous) => previous with { Progress = progress, Finished = finished, Queued = false });

        if (_subscribers.TryGetValue(progress.LibraryId, out var channels))
        {
            foreach (var channel in channels.Values)
            {
                // Dropped if the client does not read: a slow browser must not slow the scan
                // down.
                channel.Writer.TryWrite(snapshot);
            }
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ScanSnapshot> SubscribeAsync(
        string libraryId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<ScanSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var channels = _subscribers.GetOrAdd(libraryId, _ => new ConcurrentDictionary<Guid, Channel<ScanSnapshot>>());
        channels[id] = channel;

        try
        {
            // Whatever already exists goes first: a client arriving mid-scan must not stay
            // blank until the next event.
            if (_latest.TryGetValue(libraryId, out var current))
            {
                yield return current;
            }

            await foreach (var snapshot in channel.Reader.ReadAllAsync(ct))
            {
                yield return snapshot;
            }
        }
        finally
        {
            channels.TryRemove(id, out _);
            if (channels.IsEmpty)
            {
                _subscribers.TryRemove(libraryId, out _);
            }
        }
    }
}
