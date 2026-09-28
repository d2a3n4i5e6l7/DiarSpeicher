using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Imaging;

public static class SkiaImages
{
    public static (int Width, int Height)? ReadDimensions(ReadOnlySpan<byte> header)
    {
        using var data = SKData.CreateCopy(header);

        try
        {
            using var codec = SKCodec.Create(data);

            return codec is null ? null : (codec.Info.Width, codec.Info.Height);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }


    public static SKData? Encode(
        ReadOnlySpan<byte> source,
        SKEncodedImageFormat format,
        int quality,
        int? maxWidth = null)
    {
        try
        {
            using var decoded = SKBitmap.Decode(source.ToArray());
            if (decoded is null) return null;

            using var scaled = Downscale(decoded, maxWidth);
            using var image = SKImage.FromBitmap(scaled ?? decoded);

            return image.Encode(format, quality);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    public static SKData? Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);

        return image.Encode(format, quality);
    }

    public static async Task WriteAsync(SKData data, string targetPath, CancellationToken cancellationToken = default)
    {
        await using var file = File.Create(targetPath);
        await data.AsStream().CopyToAsync(file, cancellationToken);
    }

    private static SKBitmap? Downscale(SKBitmap original, int? maxWidth)
    {
        if (maxWidth is not int limit || original.Width <= limit) return null;

        var height = (int)Math.Round(original.Height * (limit / (double)original.Width));
        var info = new SKImageInfo(limit, Math.Max(1, height));

        return original.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }
}
