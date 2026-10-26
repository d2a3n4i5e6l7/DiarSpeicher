using System.IO;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Filesystem;

public sealed class LibraryScannerOptions
{
    public IOptions<StorageOptions>? Storage { get; init; }
    public IScanProgressPublisher? ProgressPublisher { get; init; }
}

public interface ILibraryScannerService
{
    Task<LibraryScanReport> ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default);
}

public partial class LibraryScannerService : ILibraryScannerService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly IThumbnailService _thumbnailService;
    private readonly IArchiveConversionService _archiveConverter;
    private readonly StorageOptions _storage;
    private readonly ILogger<LibraryScannerService> _logger;
    private readonly IScanProgressPublisher? _progressPublisher;

    /// <summary>
    /// Archive decompression and hashing are CPU-bound, so the analysis phase is spread across
    /// cores. The database phase stays single-threaded.
    /// </summary>
    private static readonly int AnalysisParallelism = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    /// Volumes per round when creating. It caps how many books are decompressed at once, which
    /// is where the memory peak of the scan lies, and it is also the grain at which new volumes
    /// reach the record.
    /// </summary>
    private const int MediaBatchSize = 2;

    private sealed record PreparedMedia(string MediaId, string Path, ProcessedBook Analysis, string? ThumbnailPath);

    /// <summary>
    /// Passed down because the creation loop knows the per-volume progress, while the per-
    /// series progress is known three levels above.
    /// </summary>
    private sealed record ScanContext(
        string LibraryId,
        IReadOnlyDictionary<string, long> StoredMtimes,
        string ThumbnailsDir,
        ScanSettings Settings,
        LibraryScanReport Report)
    {
        public Dictionary<string, long> ObservedDirMtimes { get; } = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> AllSeriesPaths { get; set; } = [];
    }

    private sealed record SeriesProgress(string LibraryId, string SeriesId, string SeriesName, int CompletedSeries, int TotalSeries);

    /// <summary>
    /// Resolved once at the start and passed down: reading it inside the loop would mean
    /// dragging the Library entity through the whole chain just to check four booleans.
    /// </summary>
    private sealed record ScanSettings(BookAnalysisOptions Analysis, bool ConvertRarToZip, bool HardDeleteConversions)
    {
        public static ScanSettings From(LibraryConfig? config)
        {
            if (config is null)
            {
                return new(
                    new BookAnalysisOptions
                    {
                        ComputeFileHash = true,
                        ComputeKoreaderHash = true,
                        ReadEmbeddedMetadata = true
                    },
                    ConvertRarToZip: false,
                    HardDeleteConversions: false);
            }

            return new(
                new BookAnalysisOptions
                {
                    ComputeFileHash = config.GenerateFileHashes,
                    ComputeKoreaderHash = config.GenerateKoreaderHashes,
                    ReadEmbeddedMetadata = config.ProcessMetadata
                },
                config.ConvertRarToZip,
                config.HardDeleteConversions);
        }
    }

    /// <summary>
    /// A CBR with a CBZ sibling is the same book twice. The CBR is discarded, whether it comes
    /// from a conversion that did not delete the original or from a copy that was already
    /// there.
    /// </summary>
    private static bool HasConvertedSibling(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".cbr", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sibling = Path.ChangeExtension(path, ".cbz");
        return sibling is not null && File.Exists(sibling);
    }

    public LibraryScannerService(
        ISqliteConnectionFactory connectionFactory,
        IDirectoryScanner directoryScanner,
        ICompositeBookProcessor bookProcessor,
        IThumbnailService thumbnailService,
        IArchiveConversionService archiveConverter,
        ILogger<LibraryScannerService> logger,
        LibraryScannerOptions? options = null)
    {
        _connectionFactory = connectionFactory;
        _directoryScanner = directoryScanner;
        _bookProcessor = bookProcessor;
        _thumbnailService = thumbnailService;
        _archiveConverter = archiveConverter;
        _logger = logger;
        _storage = options?.Storage?.Value ?? new StorageOptions();
        _progressPublisher = options?.ProgressPublisher;
    }

    private sealed record ScanProgressDetail
    {
        public int CompletedSeries { get; init; }
        public int TotalSeries { get; init; }
        public string? CurrentSeries { get; init; }
        public string? Message { get; init; }
        public int CompletedMedia { get; init; }
        public int TotalMedia { get; init; }
        public string? CurrentMedia { get; init; }
        public IReadOnlyList<string> PendingMedia { get; init; } = [];
        public string? CurrentFile { get; init; }
        public IReadOnlyList<string> ReadingFiles { get; init; } = [];
        public IReadOnlyList<DiarSpeicherMediaDto> CreatedMedia { get; init; } = [];
        public IReadOnlyList<DiarSpeicherSeriesDto> CreatedSeries { get; init; } = [];
        public string? CurrentSeriesId { get; init; }
    }

    internal sealed class MediaPathUpdate
    {
        public required string Id { get; init; }
        public required string Path { get; init; }
        public required string Extension { get; init; }
        public required string Status { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }

    internal sealed class MediaRescanUpdate
    {
        public required string Id { get; init; }
        public required long Size { get; init; }
        public required int Pages { get; init; }
        public string? Hash { get; init; }
        public string? KoreaderHash { get; init; }
        public string? ThumbnailPath { get; init; }
        public required DateTimeOffset ModifiedAt { get; init; }
        public required string Status { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }

    internal sealed record MediaOwnershipItem(string Id, string Path, string? SeriesId, DateTimeOffset CreatedAt, string? ThumbnailPath);
}
