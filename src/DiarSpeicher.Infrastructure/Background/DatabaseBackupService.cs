using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Background;

/// <summary>
/// Copies the database to disk every <see cref="BackupOptions.IntervalHours"/> hours, keeping
/// the two most recent copies in fixed slots.
/// <para>
/// The copy goes through SQLite's online backup API rather than a file copy: the database runs
/// in WAL mode, where committed data lives in the -wal file until a checkpoint, so copying the
/// main file alone would produce an almost empty backup.
/// </para>
/// </summary>
public class DatabaseBackupService : BackgroundService
{
    /// <summary>
    /// Fixed rotation slots. Each run overwrites the older of the two, so the pair always holds
    /// the two most recent backups and the directory never grows.
    /// </summary>
    private static readonly string[] SlotFileNames = ["back1.db", "back2.db"];

    // The context and its factory are both scoped, so a singleton BackgroundService reaches
    // them through a scope of its own rather than by injection.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly StorageOptions _storage;

    public DatabaseBackupService(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseBackupService> logger,
        IOptions<StorageOptions> storageOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _storage = storageOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _storage.Backup;

        if (!options.Enabled)
        {
            _logger.LogInformation("Database backups are disabled by configuration");
            return;
        }

        var directory = _storage.ResolveBackupPath();
        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));

        _logger.LogInformation(
            "Database backups every {IntervalHours}h into {Directory}, rotating {SlotCount} copies",
            interval.TotalHours,
            directory,
            SlotFileNames.Length);

        try
        {
            Directory.CreateDirectory(directory);

            // The delay is measured from the newest existing backup rather than from startup, so
            // a container that restarts often neither skips backups nor burns both slots on
            // fresh copies of the same data.
            var elapsed = TimeSinceNewestBackup(directory);
            if (elapsed is not null && elapsed < interval)
            {
                var remaining = interval - elapsed.Value;
                _logger.LogInformation(
                    "Newest backup is {ElapsedHours:F1}h old; next backup in {RemainingHours:F1}h",
                    elapsed.Value.TotalHours,
                    remaining.TotalHours);

                await Task.Delay(remaining, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunBackupAsync(directory, stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DatabaseBackupService was cancelled");
        }
        catch (Exception ex)
        {
            // The backup loop must never take the host down with it.
            _logger.LogError(ex, "DatabaseBackupService stopped after an unrecoverable error");
        }
    }

    private async Task RunBackupAsync(string directory, CancellationToken cancellationToken)
    {
        var target = SelectSlot(directory);

        // A crash mid-copy would otherwise leave a truncated file in a slot that still counts as
        // one of the two backups, so the copy lands beside the slot and is moved into place only
        // once it is complete.
        var staging = target + ".tmp";

        try
        {
            string? connectionString;
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DiarSpeicherDbContext>();
                connectionString = context.Database.GetConnectionString();
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("Skipping backup: the database has no connection string");
                return;
            }

            DeleteIfExists(staging);

            await using var source = new SqliteConnection(connectionString);
            await source.OpenAsync(cancellationToken);

            await using (var destination = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = staging }.ToString()))
            {
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }

            File.Move(staging, target, overwrite: true);

            // The backup API writes a self-contained file, so the -wal and -shm the destination
            // connection may have produced carry nothing and would only be mistaken for part of
            // the backup.
            DeleteIfExists(staging + "-wal");
            DeleteIfExists(staging + "-shm");

            _logger.LogInformation(
                "Database backed up to {Target} ({Bytes} bytes)",
                target,
                new FileInfo(target).Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed run leaves the previous two backups untouched; the next tick retries.
            _logger.LogError(ex, "Database backup to {Target} failed", target);
            DeleteIfExists(staging);
        }
    }

    /// <summary>
    /// Returns the slot to overwrite: an unused one if there is any, otherwise the oldest, which
    /// is the one whose loss still leaves the most recent backup in place.
    /// </summary>
    private static string SelectSlot(string directory)
    {
        var slots = SlotFileNames.Select(name => Path.Combine(directory, name)).ToArray();

        return slots.FirstOrDefault(slot => !File.Exists(slot))
            ?? slots.MinBy(File.GetLastWriteTimeUtc)!;
    }

    /// <summary>Age of the most recent backup, or null when no backup exists yet.</summary>
    private static TimeSpan? TimeSinceNewestBackup(string directory)
    {
        var newest = SlotFileNames
            .Select(name => Path.Combine(directory, name))
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(default)
            .Max();

        if (newest == default)
        {
            return null;
        }

        // A clock change backwards would otherwise yield a negative age and skip the wait.
        var elapsed = DateTime.UtcNow - newest;

        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
