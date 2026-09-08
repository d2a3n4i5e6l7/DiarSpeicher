using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Extensions.Logging;

namespace DiarSpeicher.Infrastructure.Filesystem.Thumbnails;

public interface IThumbnailService
{
    /// <summary>
    /// Extracts the cover page from <paramref name="mediaPath"/> and writes it as a thumbnail.
    /// Callers that already hold the cover should prefer <see cref="SaveThumbnailAsync"/>,
    /// which avoids opening and decompressing the archive a second time.
    /// </summary>
    Task<string?> GenerateThumbnailAsync(
        string mediaId,
        string mediaPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an already-extracted cover page as a thumbnail.
    /// <paramref name="outputDirectory"/> is expected to exist.
    /// </summary>
    Task<string?> SaveThumbnailAsync(
        string mediaId,
        ExtractedPage? page,
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

            return await WriteAsync(mediaId, page, outputDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnail for media {MediaId} at {Path}", mediaId, mediaPath);
            return null;
        }
    }

    public async Task<string?> SaveThumbnailAsync(
        string mediaId,
        ExtractedPage? page,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (page is null || page.Data.Length == 0)
        {
            _logger.LogDebug("No cover page available for media {MediaId}", mediaId);
            return null;
        }

        try
        {
            return await WriteAsync(mediaId, page, outputDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to save thumbnail for media {MediaId}", mediaId);
            return null;
        }
    }

    private async Task<string> WriteAsync(
        string mediaId,
        ExtractedPage page,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var ext = page.ContentType.DefaultExtension();
        if (string.IsNullOrEmpty(ext))
        {
            ext = "jpg";
        }

        var targetFilePath = Path.Combine(outputDirectory, $"{mediaId}.{ext}");

        await File.WriteAllBytesAsync(targetFilePath, page.Data, cancellationToken);
        _logger.LogTrace("Saved thumbnail for {MediaId} at {Path}", mediaId, targetFilePath);

        return targetFilePath;
    }
}
