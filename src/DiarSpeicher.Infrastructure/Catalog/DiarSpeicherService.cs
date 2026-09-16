namespace DiarSpeicher.Infrastructure.Catalog;

public sealed partial class DiarSpeicherService : IDiarSpeicherService
{
    private readonly DiarSpeicherDbContext _db;

    private readonly ICompositeBookProcessor _bookProcessor;

    private readonly IScannerQueue _scannerQueue;

    private readonly ILogger<DiarSpeicherService> _logger;

    private readonly UploadOptions _uploadOptions;

    private readonly StorageOptions _storage;

    private readonly LibraryRootsOptions _libraryRoots;

    private readonly ITrashService _trash;

    private readonly IReadingProgress _progress;

    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public DiarSpeicherService(
        DiarSpeicherDbContext db,
        ICompositeBookProcessor bookProcessor,
        IScannerQueue scannerQueue,
        ILogger<DiarSpeicherService> logger,
        IOptions<StorageOptions> storageOptions,
        IReadingProgress progress,
        DiarSpeicherServiceOptions? options = null)
    {
        _db = db;
        _bookProcessor = bookProcessor;
        _scannerQueue = scannerQueue;
        _logger = logger;
        _uploadOptions = storageOptions.Value.Upload;
        _storage = storageOptions.Value;
        _libraryRoots = options?.LibraryRoots?.Value ?? new LibraryRootsOptions();
        _trash = options?.Trash ?? new NullTrashService();
        _progress = progress;
    }
}

public sealed class DiarSpeicherServiceOptions
{
    public IOptions<LibraryRootsOptions>? LibraryRoots { get; init; }
    public ITrashService? Trash { get; init; }
}
