using DiarSpeicher.Core.Domain.Catalog;

namespace DiarSpeicher.Core.Filesystem;

public enum ScanPhase
{
    Started,
    WalkingLibrary,
    ProcessingSeries,

    ProcessingMedia,

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

    /// <summary>
    /// Volume-level progress within the current series. With series granularity alone, a
    /// single-folder library with thirty volumes spends the whole scan at "0 of 1" and the
    /// client has nothing to draw progress with.
    /// </summary>
    public int CompletedMedia { get; init; }
    public int TotalMedia { get; init; }
    public string? CurrentMedia { get; init; }

    /// <summary>
    /// Paths of the volumes the current series is about to create, in processing order. The
    /// path and not the name, because names repeat: two different folders each have their own
    /// "Volume 1", and keyed by name the client would take them for the same volume.
    /// </summary>
    public IReadOnlyList<string> PendingMedia { get; init; } = [];

    /// <summary>
    /// File name of the volume being read, in the same form as <see cref="PendingMedia"/>. <see
    /// cref="CurrentMedia"/> cannot be used to match them: when the volume carries metadata it
    /// holds the title, not the file name.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Files the scanner is reading right now, by name, like <see cref="PendingMedia"/>. They
    /// are announced before the analysis, which is the slow part, so the slot lights up while
    /// reading rather than once it has finished.
    /// </summary>
    public IReadOnlyList<string> ReadingFiles { get; init; } = [];

    /// <summary>
    /// Volumes just added to the catalog, already mapped. They are sent in full so the client
    /// draws the final card (cover, pages and size) as soon as the event arrives, without
    /// refetching the whole catalog every time a series finishes.
    /// </summary>
    public IReadOnlyList<DiarSpeicherMediaDto> CreatedMedia { get; init; } = [];

    /// <summary>Series just added to the catalog, sent in full for the same reason.</summary>
    public IReadOnlyList<DiarSpeicherSeriesDto> CreatedSeries { get; init; } = [];

    /// <summary>Identifies the series without relying on the name, which is not unique.</summary>
    public string? CurrentSeriesId { get; init; }

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
