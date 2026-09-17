using System.Text.Json;
using DiarSpeicher.Infrastructure.Metadata;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Infrastructure.Background;

public sealed class PartialUploadPurgeService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions MetaJson = new() { PropertyNameCaseInsensitive = true };

    private readonly IOptions<StorageOptions> _storage;
    private readonly IOptions<MangaBakaOptions> _mangaBaka;
    private readonly ILogger<PartialUploadPurgeService> _logger;

    public PartialUploadPurgeService(
        IOptions<StorageOptions> storage,
        IOptions<MangaBakaOptions> mangaBaka,
        ILogger<PartialUploadPurgeService> logger)
    {
        _storage = storage;
        _mangaBaka = mangaBaka;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deadline = DateTime.UtcNow - Retention;
                var borrados = PurgeTusUploads(deadline) + PurgeMetadataImports(deadline);

                if (borrados > 0)
                {
                    _logger.LogInformation("Barridas {Count} subidas abandonadas de mas de {Hours} h", borrados, Retention.TotalHours);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "El barrido de subidas abandonadas no pudo completarse");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private TimeSpan Retention =>
        TimeSpan.FromHours(Math.Max(1, _storage.Value.Upload.AbandonedUploadHours));

    private int PurgeTusUploads(DateTime deadline)
    {
        var uploadsDir = _storage.Value.ResolveUploadsPath();
        if (!Directory.Exists(uploadsDir)) return 0;

        var borrados = 0;

        foreach (var metaPath in Directory.EnumerateFiles(uploadsDir, "*.meta"))
        {
            var partPath = ReadPartPath(metaPath);

            if (partPath != null && File.Exists(partPath))
            {
                if (File.GetLastWriteTimeUtc(partPath) > deadline) continue;
                if (!TryDelete(partPath)) continue;
            }
            else if (File.GetLastWriteTimeUtc(metaPath) > deadline)
            {
                continue;
            }

            if (TryDelete(metaPath)) borrados++;
        }

        return borrados;
    }

    private int PurgeMetadataImports(DateTime deadline)
    {
        var dbDir = _mangaBaka.Value.ResolveDatabasePath();
        if (!Directory.Exists(dbDir)) return 0;

        var borrados = 0;

        foreach (var part in Directory.EnumerateFiles(dbDir, "import_*.part"))
        {
            if (File.GetLastWriteTimeUtc(part) > deadline) continue;
            if (TryDelete(part)) borrados++;
        }

        return borrados;
    }

    private string? ReadPartPath(string metaPath)
    {
        try
        {
            var meta = JsonSerializer.Deserialize<UploadMeta>(File.ReadAllText(metaPath), MetaJson);
            return string.IsNullOrWhiteSpace(meta?.PartPath) ? null : meta.PartPath;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Meta de subida ilegible: {Meta}", metaPath);
            return null;
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "No se pudo borrar {Path}", path);
            return false;
        }
    }

    private sealed class UploadMeta
    {
        public string? PartPath { get; set; }
    }
}
