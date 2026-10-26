using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Imaging;
using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Thumbnails;

public interface IThumbnailService
{
    Task<string?> GenerateThumbnailAsync(
        string mediaId,
        string mediaPath,
        string outputDirectory,
        CancellationToken cancellationToken = default);

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
    private readonly ThumbnailOptions _options;

    public ThumbnailService(
        ICompositeBookProcessor bookProcessor,
        ILogger<ThumbnailService> logger,
        IOptions<StorageOptions>? storageOptions = null)
    {
        _bookProcessor = bookProcessor;
        _logger = logger;
        _options = storageOptions?.Value.Thumbnails ?? new ThumbnailOptions();
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
        Directory.CreateDirectory(outputDirectory);

        var format = _options.PreferJpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Webp;

        using var encoded = SkiaImages.Encode(page.Data, format, _options.Quality, _options.MaxWidth);
        if (encoded is null)
        {
            return await WriteUnchangedAsync(mediaId, page, outputDirectory, cancellationToken);
        }

        var extension = _options.PreferJpeg ? "jpg" : "webp";
        var targetFilePath = Path.Combine(outputDirectory, $"{mediaId}.{extension}");
        await SkiaImages.WriteAsync(encoded, targetFilePath, cancellationToken);
        _logger.LogTrace("Saved thumbnail for {MediaId} at {Path}", mediaId, targetFilePath);

        return targetFilePath;
    }

    /// <summary>
    /// Stores the cover as it came when Skia could not read it: something the client may be
    /// able to display beats leaving the volume without a cover.
    /// </summary>
    private async Task<string> WriteUnchangedAsync(
        string mediaId,
        ExtractedPage page,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var extension = page.ContentType.DefaultExtension();
        if (string.IsNullOrEmpty(extension))
        {
            extension = "jpg";
        }

        _logger.LogDebug("Could not re-encode the cover; storing it unchanged as .{Extension}", extension);

        var targetFilePath = Path.Combine(outputDirectory, $"{mediaId}.{extension}");
        await File.WriteAllBytesAsync(targetFilePath, page.Data, cancellationToken);

        return targetFilePath;
    }
}
