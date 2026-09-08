using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Infrastructure.Storage;

/// <summary>
/// Decorates <see cref="ICompositeBookProcessor"/> with a disk page cache.
/// Page extraction is CPU-bound decompression repeated on every read of the same page,
/// so serving it from cache keeps that work off the request path entirely.
/// Analysis is not cached: it runs once per file during a scan.
/// </summary>
public class CachingBookProcessor : ICompositeBookProcessor
{
    private readonly ICompositeBookProcessor _inner;
    private readonly IPageCache _cache;

    public CachingBookProcessor(ICompositeBookProcessor inner, IPageCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public IBookProcessor? GetProcessor(string path) => _inner.GetProcessor(path);

    public Task<ProcessedBook> AnalyzeAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default) =>
        _inner.AnalyzeAsync(path, includeCover, cancellationToken);

    public async Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(path, pageNumber);
        if (key is null)
        {
            return await _inner.ExtractPageAsync(path, pageNumber, cancellationToken);
        }

        var cached = await _cache.GetAsync(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var page = await _inner.ExtractPageAsync(path, pageNumber, cancellationToken);
        if (page is not null && page.Data.Length > 0)
        {
            await _cache.SetAsync(key, page, cancellationToken);
        }

        return page;
    }

    /// <summary>
    /// Keys include the file's size and last write time, so editing or replacing a book
    /// invalidates its cached pages instead of serving stale ones.
    /// </summary>
    private static string? BuildKey(string path, int pageNumber)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        return $"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|p{pageNumber}";
    }
}
