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

    /// <summary>Lo unico que se acepta como portada subida a mano.</summary>
    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    public DiarSpeicherService(
        DiarSpeicherDbContext db,
        ICompositeBookProcessor bookProcessor,
        IScannerQueue scannerQueue,
        ILogger<DiarSpeicherService> logger,
        IOptions<StorageOptions> storageOptions,
        IOptions<LibraryRootsOptions>? libraryRoots = null,
        ITrashService? trash = null)
    {
        _db = db;
        _bookProcessor = bookProcessor;
        _scannerQueue = scannerQueue;
        _logger = logger;
        _uploadOptions = storageOptions.Value.Upload;
        _storage = storageOptions.Value;
        _libraryRoots = libraryRoots?.Value ?? new LibraryRootsOptions();
        _trash = trash ?? new NullTrashService();
    }
}
