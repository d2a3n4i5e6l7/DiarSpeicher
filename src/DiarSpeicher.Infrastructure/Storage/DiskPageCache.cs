using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DiarSpeicher.Core.Filesystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Storage;

public interface IPageCache
{
    Task<ExtractedPage?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, ExtractedPage page, CancellationToken cancellationToken = default);
}

/// <summary>
/// Disk-backed page cache with least-recently-used eviction under a total size budget.
/// Entries survive restarts: the index is rebuilt from the cache directory at startup,
/// seeding recency from each file's last write time.
/// </summary>
public sealed class DiskPageCache : IPageCache
{
    private sealed class Entry
    {
        public required string FileName { get; init; }
        public required long Size { get; init; }

        /// <summary>
        /// Monotonic access stamp. A wall-clock timestamp would tie under bursts, because
        /// many writes can land within one clock tick, which degrades LRU to arbitrary order.
        /// </summary>
        public long Recency;
    }

    private readonly ConcurrentDictionary<string, Entry> _index = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _evictionLock = new(1, 1);
    private readonly PageCacheOptions _options;
    private readonly string _directory;
    private readonly ILogger<DiskPageCache> _logger;
    private long _totalBytes;
    private long _accessSequence;

    public DiskPageCache(IOptions<StorageOptions> options, ILogger<DiskPageCache> logger)
    {
        var storage = options.Value;
        _options = storage.PageCache;
        _directory = storage.ResolvePageCachePath();
        _logger = logger;

        if (_options.Enabled)
        {
            LoadExistingEntries();
        }
    }

    /// <summary>Total bytes currently held on disk. Exposed for tests and diagnostics.</summary>
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    public async Task<ExtractedPage?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;

        var id = HashKey(key);
        if (!_index.TryGetValue(id, out var entry))
        {
            return null;
        }

        var path = Path.Combine(_directory, entry.FileName);
        try
        {
            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            Interlocked.Exchange(ref entry.Recency, NextSequence());

            var contentType = ContentTypeExtensions.FromExtension(Path.GetExtension(entry.FileName));
            return new ExtractedPage(contentType, data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The file vanished or is unreadable: drop it from the index and treat as a miss.
            Forget(id);
            _logger.LogDebug(ex, "Dropping unreadable page cache entry {File}", entry.FileName);
            return null;
        }
    }

    public async Task SetAsync(string key, ExtractedPage page, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        if (page.Data.Length == 0 || page.Data.Length > _options.MaxEntryBytes) return;

        var id = HashKey(key);
        var ext = page.ContentType.DefaultExtension();
        if (string.IsNullOrEmpty(ext)) ext = "bin";

        var fileName = $"{id}.{ext}";
        var finalPath = Path.Combine(_directory, fileName);
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(_directory);

            // Write to a temp file and move into place, so a reader never observes a
            // half-written page.
            await File.WriteAllBytesAsync(tempPath, page.Data, cancellationToken);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write page cache entry {File}", fileName);
            TryDelete(tempPath);
            return;
        }

        Remember(id, fileName, page.Data.Length);
        await EvictIfNeededAsync();
    }

    private void LoadExistingEntries()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;

            var loaded = new List<(string Id, FileInfo Info)>();

            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    TryDelete(file);
                    continue;
                }

                var info = new FileInfo(file);
                var id = Path.GetFileNameWithoutExtension(file);

                loaded.Add((id, info));
            }

            // Seed recency from write times so the first eviction after a restart still
            // discards the oldest entries.
            foreach (var (id, info) in loaded.OrderBy(x => x.Info.LastWriteTimeUtc))
            {
                var entry = new Entry
                {
                    FileName = info.Name,
                    Size = info.Length,
                    Recency = NextSequence()
                };

                if (_index.TryAdd(id, entry))
                {
                    Interlocked.Add(ref _totalBytes, info.Length);
                }
            }

            _logger.LogInformation(
                "Page cache loaded {Count} entries ({Bytes} bytes) from {Directory}",
                _index.Count, TotalBytes, _directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load existing page cache from {Directory}", _directory);
        }
    }

    private void Remember(string id, string fileName, long size)
    {
        var entry = new Entry
        {
            FileName = fileName,
            Size = size,
            Recency = NextSequence()
        };

        if (_index.TryGetValue(id, out var previous))
        {
            // Overwriting an existing entry: only the delta counts toward the budget.
            Interlocked.Add(ref _totalBytes, size - previous.Size);
        }
        else
        {
            Interlocked.Add(ref _totalBytes, size);
        }

        _index[id] = entry;
    }

    private void Forget(string id)
    {
        if (_index.TryRemove(id, out var removed))
        {
            Interlocked.Add(ref _totalBytes, -removed.Size);
        }
    }

    private async Task EvictIfNeededAsync()
    {
        if (TotalBytes <= _options.MaxBytes) return;

        // One evictor at a time; concurrent writers just proceed, and the next write
        // re-checks the budget.
        if (!await _evictionLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var target = (long)(_options.MaxBytes * _options.EvictionTargetRatio);
            if (TotalBytes <= _options.MaxBytes) return;

            var candidates = _index
                .Select(kvp => (Id: kvp.Key, kvp.Value.FileName, Recency: Interlocked.Read(ref kvp.Value.Recency)))
                .OrderBy(c => c.Recency)
                .ToList();

            var evicted = 0;
            foreach (var candidate in candidates)
            {
                if (TotalBytes <= target) break;

                Forget(candidate.Id);
                TryDelete(Path.Combine(_directory, candidate.FileName));
                evicted++;
            }

            if (evicted > 0)
            {
                _logger.LogInformation(
                    "Page cache evicted {Count} least-recently-used entries, now {Bytes} bytes",
                    evicted, TotalBytes);
            }
        }
        finally
        {
            _evictionLock.Release();
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete page cache file {Path}", path);
        }
    }

    private long NextSequence() => Interlocked.Increment(ref _accessSequence);

    private static string HashKey(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
