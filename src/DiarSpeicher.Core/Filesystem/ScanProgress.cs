namespace DiarSpeicher.Core.Filesystem;

public enum ScanPhase
{
    Started,
    WalkingLibrary,
    ProcessingSeries,
    Completed,
    Failed
}

/// <summary>
/// Incremental progress for one scan. <see cref="JobId"/> is the library id, so a client
/// subscribing to a scan it triggered knows the id up front.
/// </summary>
public record ScanProgressEvent
{
    public string JobId { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
    public ScanPhase Phase { get; init; }
    public int CompletedSeries { get; init; }
    public int TotalSeries { get; init; }
    public string? CurrentSeries { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>0 when the series count is not yet known, so a client can render a spinner.</summary>
    public double Percentage => TotalSeries <= 0
        ? 0
        : Math.Round(CompletedSeries / (double)TotalSeries * 100, 2);
}

public interface IScanProgressPublisher
{
    ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default);
}
