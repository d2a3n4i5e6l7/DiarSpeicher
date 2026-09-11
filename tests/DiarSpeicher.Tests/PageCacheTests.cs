using System.IO.Compression;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class PageCacheTests : IDisposable
{
    private readonly string _tempDir;

    public PageCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "diarspeicher_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Ignore cleanup errors in temp dir
        }
        GC.SuppressFinalize(this);
    }

    private DiskPageCache CreateCache(Action<PageCacheOptions>? configure = null)
    {
        var options = new StorageOptions { RootPath = _tempDir };
        configure?.Invoke(options.PageCache);
        return new DiskPageCache(Options.Create(options), NullLogger<DiskPageCache>.Instance);
    }

    private string CreateCbz(string name, int pageCount, byte marker)
    {
        var path = Path.Combine(_tempDir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        for (int i = 1; i <= pageCount; i++)
        {
            var entry = zip.CreateEntry($"{i:D3}.jpg");
            using var s = entry.Open();
            s.Write([0xFF, 0xD8, 0xFF, 0xE0, (byte)i, marker]);
        }
        return path;
    }

    [Fact]
    public async Task DiskPageCache_RoundTrips_PageAndContentType()
    {
        var cache = CreateCache();
        var page = new ExtractedPage(ContentType.Png, [0x89, 0x50, 0x4E, 0x47, 0x01]);

        Assert.Null(await cache.GetAsync("missing-key"));

        await cache.SetAsync("key-1", page);
        var hit = await cache.GetAsync("key-1");

        Assert.NotNull(hit);
        Assert.Equal(page.Data, hit.Data);
        Assert.Equal(ContentType.Png, hit.ContentType);
    }

    [Fact]
    public async Task DiskPageCache_EvictsLeastRecentlyUsed_WhenOverBudget()
    {
        // Budget fits roughly 10 entries of 1 KB; eviction trims down to 90%.
        var cache = CreateCache(o =>
        {
            o.MaxBytes = 10 * 1024;
            o.MaxEntryBytes = 4 * 1024;
            o.EvictionTargetRatio = 0.5;
        });

        var payload = new byte[1024];

        // Fill the cache to its limit.
        for (int i = 0; i < 10; i++)
        {
            await cache.SetAsync($"key-{i}", new ExtractedPage(ContentType.Jpeg, payload));
        }

        Assert.True(cache.TotalBytes <= 10 * 1024);

        // Touch key-0 so it becomes the most recently used entry.
        Assert.NotNull(await cache.GetAsync("key-0"));

        // Push past the limit to force eviction.
        for (int i = 10; i < 16; i++)
        {
            await cache.SetAsync($"key-{i}", new ExtractedPage(ContentType.Jpeg, payload));
        }

        Assert.True(cache.TotalBytes <= 10 * 1024, $"Cache exceeded its budget: {cache.TotalBytes} bytes");

        // The recently touched entry and the newest one survived; something older did not.
        Assert.NotNull(await cache.GetAsync("key-15"));
        Assert.Null(await cache.GetAsync("key-1"));
    }

    [Fact]
    public async Task DiskPageCache_SkipsEntriesLargerThanMaxEntryBytes()
    {
        var cache = CreateCache(o => o.MaxEntryBytes = 64);

        await cache.SetAsync("big", new ExtractedPage(ContentType.Jpeg, new byte[256]));

        Assert.Null(await cache.GetAsync("big"));
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public async Task DiskPageCache_Disabled_NeverStoresAnything()
    {
        var cache = CreateCache(o => o.Enabled = false);

        await cache.SetAsync("key", new ExtractedPage(ContentType.Jpeg, [1, 2, 3]));

        Assert.Null(await cache.GetAsync("key"));
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public async Task DiskPageCache_ReloadsExistingEntries_AcrossRestarts()
    {
        var first = CreateCache();
        await first.SetAsync("persisted", new ExtractedPage(ContentType.Jpeg, [1, 2, 3, 4]));

        var second = CreateCache();
        var hit = await second.GetAsync("persisted");

        Assert.NotNull(hit);
        Assert.Equal([1, 2, 3, 4], hit.Data);
    }

    [Fact]
    public async Task CachingBookProcessor_ServesSecondReadFromCache()
    {
        var bookPath = CreateCbz("cached.cbz", pageCount: 3, marker: 0xAA);
        var cache = CreateCache();
        var inner = new CountingProcessor(new CompositeBookProcessor([new ZipBookProcessor()]));
        var processor = new CachingBookProcessor(inner, cache);

        var first = await processor.ExtractPageAsync(bookPath, 2);
        var second = await processor.ExtractPageAsync(bookPath, 2);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Data, second.Data);

        // The archive was only decompressed once.
        Assert.Equal(1, inner.ExtractCalls);
    }

    [Fact]
    public async Task CachingBookProcessor_InvalidatesWhenTheBookChangesOnDisk()
    {
        var bookPath = CreateCbz("mutating.cbz", pageCount: 2, marker: 0x01);
        var cache = CreateCache();
        var inner = new CountingProcessor(new CompositeBookProcessor([new ZipBookProcessor()]));
        var processor = new CachingBookProcessor(inner, cache);

        var before = await processor.ExtractPageAsync(bookPath, 1);
        Assert.NotNull(before);

        // Replace the book with different content.
        File.Delete(bookPath);
        CreateCbz("mutating.cbz", pageCount: 2, marker: 0x02);
        File.SetLastWriteTimeUtc(bookPath, DateTime.UtcNow.AddSeconds(5));

        var after = await processor.ExtractPageAsync(bookPath, 1);

        Assert.NotNull(after);
        Assert.NotEqual(before.Data, after.Data);
        Assert.Equal(2, inner.ExtractCalls);
    }

    private sealed class CountingProcessor : ICompositeBookProcessor
    {
        private readonly ICompositeBookProcessor _inner;
        public int ExtractCalls;

        public CountingProcessor(ICompositeBookProcessor inner) => _inner = inner;

        IBookProcessor? ICompositeBookProcessor.GetProcessor(string path) => _inner.GetProcessor(path);

        Task<ProcessedBook> ICompositeBookProcessor.AnalyzeAsync(string path, bool includeCover, CancellationToken cancellationToken, bool measurePages) =>
            _inner.AnalyzeAsync(path, includeCover, cancellationToken, measurePages);

        public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ExtractCalls);
            return _inner.ExtractPageAsync(path, pageNumber, cancellationToken);
        }
    }
}
