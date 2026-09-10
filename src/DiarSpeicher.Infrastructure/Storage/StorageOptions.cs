namespace DiarSpeicher.Infrastructure.Storage;

/// <summary>
/// Filesystem locations for data DiarSpeicher generates (as opposed to the user's libraries).
/// Bound from the "Storage" configuration section.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Root for all generated data. Defaults to "storage" under the process working
    /// directory, which is where thumbnails already lived before this was configurable.
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "storage");

    /// <summary>Overrides the thumbnail directory. Defaults to "&lt;RootPath&gt;/thumbnails".</summary>
    public string? ThumbnailsPath { get; set; }

    public PageCacheOptions PageCache { get; set; } = new();

    public ThumbnailOptions Thumbnails { get; set; } = new();

    public UploadOptions Upload { get; set; } = new();

    public WatcherOptions Watcher { get; set; } = new();

    public BackupOptions Backup { get; set; } = new();

    /// <summary>
    /// Always absolute: thumbnail paths are persisted in the database, so a path relative
    /// to the process working directory would break whenever the process is started
    /// from somewhere else.
    /// </summary>
    public string ResolveThumbnailsPath() =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(ThumbnailsPath)
            ? Path.Combine(RootPath, "thumbnails")
            : ThumbnailsPath);

    public string ResolvePageCachePath() =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(PageCache.Path)
            ? Path.Combine(RootPath, "cache", "pages")
            : PageCache.Path);

    public string ResolveBackupPath() =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(Backup.Path)
            ? Path.Combine(RootPath, "backups")
            : Backup.Path);

    public string ResolveUploadsPath() =>
        Path.GetFullPath(Path.Combine(RootPath, "cache", "uploads"));
}

/// <summary>
/// Scheduled copies of the database. Only two are kept, in the fixed slots back1.db and
/// back2.db, so the backup directory has a bounded size and no cleanup job of its own.
/// </summary>
public class BackupOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Overrides the backup directory. Defaults to "&lt;RootPath&gt;/backups".</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Hours between backups, at least 1. Default 12: with two slots that covers the last 12
    /// to 24 hours, so a corruption noticed the same day still has a copy from before it.
    /// </summary>
    public int IntervalHours { get; set; } = 12;
}

/// <summary>
/// Thumbnail generation. Covers come out of the archive at full page resolution, which is
/// far larger than any grid or list view needs, so they are downscaled and re-encoded
/// rather than served as extracted.
/// </summary>
public class ThumbnailOptions
{
    /// <summary>
    /// Maximum width in pixels. Aspect ratio is preserved and a cover already narrower than
    /// this is never upscaled.
    /// </summary>
    public int MaxWidth { get; set; } = 512;

    /// <summary>WebP quality, 1-100. Default 80: visually clean at a fraction of the size.</summary>
    public int Quality { get; set; } = 80;

    /// <summary>
    /// Falls back to JPEG instead of WebP, for clients that cannot display WebP.
    /// </summary>
    public bool PreferJpeg { get; set; }
}

/// <summary>
/// Disk cache for extracted book pages. Decompressing a page is CPU-bound work repeated on
/// every read of the same page, so results are kept on disk and evicted least-recently-used
/// once the cache exceeds <see cref="MaxBytes"/>.
/// </summary>
public class PageCacheOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Overrides the cache directory. Defaults to "&lt;RootPath&gt;/cache/pages".</summary>
    public string? Path { get; set; }

    /// <summary>Upper bound on total cache size. Default 2 GiB.</summary>
    public long MaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Pages larger than this are served but never cached, so one oversized page cannot
    /// evict the rest of the cache. Default 32 MiB.
    /// </summary>
    public long MaxEntryBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>
    /// Eviction runs down to this fraction of <see cref="MaxBytes"/> rather than stopping at
    /// the limit, so a full cache does not evict on every single write. Default 0.9.
    /// </summary>
    public double EvictionTargetRatio { get; set; } = 0.9;
}

/// <summary>
/// Filesystem watching, so copying a file into a library indexes it without a manual scan.
/// </summary>
public class WatcherOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Quiet period after the last event before a scan is queued. Copying a large volume
    /// raises events throughout the copy, and scanning a half-written file finds a corrupt
    /// archive, so the scan waits for the writes to stop. Default 10 s.
    /// </summary>
    public int DebounceSeconds { get; set; } = 10;

    /// <summary>
    /// Watch only each library root instead of recursing. Escape hatch for hosts whose
    /// fs.inotify.max_user_watches cannot cover a library with thousands of directories.
    /// </summary>
    public bool WatchRootsOnly { get; set; }
}

/// <summary>
/// Limits for library file uploads. Book volumes routinely exceed the ASP.NET Core
/// defaults (Kestrel caps request bodies at 30 MB and multipart form bodies at 128 MB),
/// so the upload endpoint raises both to <see cref="MaxRequestBytes"/>.
/// </summary>
public class UploadOptions
{
    /// <summary>
    /// Largest accepted upload request, covering all files in one multipart batch.
    /// Default 1 GiB: comfortably fits comic and book volumes without letting a single
    /// request fill the disk by default.
    /// </summary>
    public long MaxRequestBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>Disables the upload endpoint entirely. Environment: DIAR_ENABLE_UPLOAD.</summary>
    public bool EnableUpload { get; set; } = true;

    /// <summary>
    /// Largest accepted size for a single file, as opposed to
    /// <see cref="MaxRequestBytes"/>, which bounds the whole multipart request.
    /// Default 500 MB. Environment: DIAR_MAX_FILE_UPLOAD_SIZE.
    /// </summary>
    public long MaxFileUploadSize { get; set; } = 524_288_000;

    public static readonly string[] DefaultAllowedExtensions = [".cbz", ".cbr", ".epub", ".pdf", ".zip"];

    /// <summary>
    /// Extensions accepted by the upload endpoint, leading dot included and compared
    /// case-insensitively. Environment: DIAR_ALLOWED_EXTENSIONS (comma separated).
    /// Left empty by default because configuration binding appends to a list rather than
    /// replacing it, which would duplicate every pre-seeded default; an empty list falls
    /// back to <see cref="DefaultAllowedExtensions"/> when read.
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = [];

    public IReadOnlyList<string> ResolveAllowedExtensions() =>
        AllowedExtensions.Count > 0 ? AllowedExtensions : DefaultAllowedExtensions;

    public bool IsExtensionAllowed(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension)) return false;

        return ResolveAllowedExtensions().Any(allowed =>
            NormalizeExtension(allowed).Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();

        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
