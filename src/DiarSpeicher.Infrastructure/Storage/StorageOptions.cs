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
