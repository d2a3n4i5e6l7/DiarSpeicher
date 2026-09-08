using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

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
        var encoded = await EncodeAsync(page, cancellationToken);
        var targetFilePath = Path.Combine(outputDirectory, $"{mediaId}.{encoded.Extension}");

        await File.WriteAllBytesAsync(targetFilePath, encoded.Data, cancellationToken);
        _logger.LogTrace("Saved thumbnail for {MediaId} at {Path}", mediaId, targetFilePath);

        return targetFilePath;
    }

    /// <summary>
    /// Downscales to <see cref="ThumbnailOptions.MaxWidth"/> preserving the aspect ratio and
    /// re-encodes to WebP, which is where the order-of-magnitude size reduction comes from.
    /// A cover that cannot be decoded is written through unchanged so a thumbnail still
    /// exists, rather than failing the scan over an unsupported format.
    /// </summary>
    private Task<EncodedThumbnail> EncodeAsync(ExtractedPage page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var original = SKBitmap.Decode(page.Data);
            if (original is null)
            {
                return Task.FromResult(Fallback(page, reason: null));
            }

            using var resized = Downscale(original);
            using var image = SKImage.FromBitmap(resized ?? original);

            var format = _options.PreferJpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Webp;
            using var encoded = image.Encode(format, _options.Quality);
            if (encoded is null)
            {
                return Task.FromResult(Fallback(page, reason: null));
            }

            return Task.FromResult(new EncodedThumbnail(
                encoded.ToArray(),
                _options.PreferJpeg ? "jpg" : "webp"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(Fallback(page, ex));
        }
    }

    /// <summary>
    /// Returns null when the cover is already at or below the target width: a thumbnail is
    /// never upscaled, since that only inflates the file without adding detail.
    /// </summary>
    private SKBitmap? Downscale(SKBitmap original)
    {
        if (original.Width <= _options.MaxWidth)
        {
            return null;
        }

        var height = (int)Math.Round(original.Height * (_options.MaxWidth / (double)original.Width));
        var info = new SKImageInfo(_options.MaxWidth, Math.Max(1, height));

        return original.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    private EncodedThumbnail Fallback(ExtractedPage page, Exception? reason)
    {
        var extension = page.ContentType.DefaultExtension();
        if (string.IsNullOrEmpty(extension))
        {
            extension = "jpg";
        }

        _logger.LogDebug(reason, "Could not re-encode the cover; storing it unchanged as .{Extension}", extension);

        return new EncodedThumbnail(page.Data, extension);
    }

    private readonly record struct EncodedThumbnail(byte[] Data, string Extension);
}
