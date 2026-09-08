using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Filesystem.Thumbnails;

public interface IThumbnailService
{
    Task<string?> GenerateThumbnailAsync(
        string mediaId,
        string mediaPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}

public class ThumbnailService : IThumbnailService
{
    private readonly ICompositeBookProcessor _bookProcessor;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(
        ICompositeBookProcessor bookProcessor,
        ILogger<ThumbnailService> logger)
    {
        _bookProcessor = bookProcessor;
        _logger = logger;
    }

    public async Task<string?> GenerateThumbnailAsync(
        string mediaId,
        string mediaPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);

            var page = await _bookProcessor.ExtractPageAsync(mediaPath, 1, cancellationToken);
            if (page is null || page.Data.Length == 0)
            {
                _logger.LogDebug("Could not extract cover page for {Path}", mediaPath);
                return null;
            }

            var ext = page.ContentType.DefaultExtension();
            if (string.IsNullOrEmpty(ext))
            {
                ext = "jpg";
            }

            var targetFileName = $"{mediaId}.{ext}";
            var targetFilePath = Path.Combine(outputDirectory, targetFileName);

            await File.WriteAllBytesAsync(targetFilePath, page.Data, cancellationToken);
            _logger.LogTrace("Saved thumbnail for {MediaId} at {Path}", mediaId, targetFilePath);

            return targetFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for media {MediaId} at {Path}", mediaId, mediaPath);
            return null;
        }
    }
}
