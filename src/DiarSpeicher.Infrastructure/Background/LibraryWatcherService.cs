using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Watches every library path and queues a scan once writes have stopped. Events are
/// coalesced per library, so copying twenty volumes queues one scan rather than twenty.
/// </summary>
public class LibraryWatcherService : BackgroundService
{
    private readonly IScannerQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LibraryWatcherService> _logger;
    private readonly WatcherOptions _options;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, DateTimeOffset> _pending = [];
    private readonly Lock _pendingLock = new();

    public LibraryWatcherService(
        IScannerQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<LibraryWatcherService> logger,
        IOptions<StorageOptions> storageOptions)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = storageOptions.Value.Watcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Library watching is disabled by configuration");
            return;
        }

        await StartWatchersAsync(stoppingToken);

        if (_watchers.Count == 0)
        {
            _logger.LogInformation("No library could be watched; scans stay manual");
            return;
        }

        var debounce = TimeSpan.FromSeconds(Math.Max(1, _options.DebounceSeconds));
        var pollInterval = TimeSpan.FromSeconds(1);

        try
        {
            using var timer = new PeriodicTimer(pollInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var libraryId in TakeSettled(debounce))
                {
                    _logger.LogInformation("Filesystem changes detected in library {LibraryId}; queueing scan", libraryId);
                    await _queue.QueueScanAsync(new ScanRequest(libraryId), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "LibraryWatcherService was cancelled");
        }
    }

    private async Task StartWatchersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();

        var libraries = await db.Libraries
            .AsNoTracking()
            .Where(l => l.Status != FileStatus.Missing)
            .Select(l => new { l.Id, l.Path })
            .ToListAsync(ct);

        foreach (var library in libraries)
        {
            if (!Directory.Exists(library.Path))
            {
                _logger.LogWarning("Library {LibraryId} path does not exist and will not be watched: {Path}", library.Id, library.Path);
                continue;
            }

            TryCreateWatcher(library.Id, library.Path);
        }
    }

    /// <summary>
    /// Recursive watching is what exhausts fs.inotify.max_user_watches on large libraries.
    /// When that happens the watcher is retried non-recursively so the library root is still
    /// covered, and the operator is told how to restore full coverage.
    /// </summary>
    private void TryCreateWatcher(string libraryId, string path)
    {
        var recursive = !_options.WatchRootsOnly;

        try
        {
            _watchers.Add(CreateWatcher(libraryId, path, recursive));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!recursive)
            {
                _logger.LogError(ex, "Could not watch library {LibraryId} at {Path}", libraryId, path);
                return;
            }

            _logger.LogWarning(
                ex,
                "Could not watch library {LibraryId} recursively at {Path}. Falling back to the root directory only: " +
                "new files in subdirectories will not trigger a scan. Raise fs.inotify.max_user_watches to restore full coverage.",
                libraryId,
                path);

            try
            {
                _watchers.Add(CreateWatcher(libraryId, path, includeSubdirectories: false));
            }
            catch (Exception fallbackEx) when (fallbackEx is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(fallbackEx, "Could not watch library {LibraryId} at {Path}", libraryId, path);
            }
        }
    }

    private FileSystemWatcher CreateWatcher(string libraryId, string path, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        watcher.Created += (_, e) => OnChanged(libraryId, e.FullPath);
        watcher.Changed += (_, e) => OnChanged(libraryId, e.FullPath);
        watcher.Deleted += (_, e) => OnChanged(libraryId, e.FullPath);
        watcher.Renamed += (_, e) => OnChanged(libraryId, e.FullPath);
        watcher.Error += (_, e) => _logger.LogWarning(e.GetException(), "Watcher error on library {LibraryId}", libraryId);

        watcher.EnableRaisingEvents = true;

        _logger.LogInformation(
            "Watching library {LibraryId} at {Path} (recursive: {Recursive})",
            libraryId,
            path,
            includeSubdirectories);

        return watcher;
    }

    /// <summary>
    /// Each event pushes the library's deadline forward, so the scan only runs once the
    /// copy has been quiet for the whole debounce period.
    /// </summary>
    private void OnChanged(string libraryId, string fullPath)
    {
        if (!IsRelevant(fullPath))
        {
            return;
        }

        lock (_pendingLock)
        {
            _pending[libraryId] = DateTimeOffset.UtcNow;
        }
    }

    private static bool IsRelevant(string fullPath)
    {
        if (Directory.Exists(fullPath))
        {
            return true;
        }

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(fileName) || PathUtils.IsHiddenFile(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();

        return PathUtils.AcceptedMediaExtensions.Contains(extension);
    }

    private List<string> TakeSettled(TimeSpan debounce)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_pendingLock)
        {
            var settled = _pending
                .Where(entry => now - entry.Value >= debounce)
                .Select(entry => entry.Key)
                .ToList();

            foreach (var libraryId in settled)
            {
                _pending.Remove(libraryId);
            }

            return settled;
        }
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
