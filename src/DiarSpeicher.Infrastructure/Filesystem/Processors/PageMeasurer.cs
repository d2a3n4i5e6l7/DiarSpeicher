using System.Buffers;
using DiarSpeicher.Infrastructure.Filesystem.Imaging;
using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public static class PageMeasurer
{
    public const int HeaderBytes = 64 * 1024;

    public static async Task<(int? Width, int? Height)> MeasureAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(HeaderBytes);
        try
        {
            var total = 0;

            while (total < HeaderBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, HeaderBytes - total), cancellationToken);
                if (read == 0) break;
                total += read;
            }

            if (total == 0) return (null, null);

            return Measure(buffer.AsSpan(0, total));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static (int? Width, int? Height) Measure(ReadOnlySpan<byte> header)
    {
        if (ImageHeader.TryRead(header, out var w, out var h)) return (w, h);

        try
        {
            if (SkiaImages.ReadDimensions(header) is not (int width, int height)) return (null, null);
            if (width <= 0 || height <= 0) return (null, null);

            return (width, height);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    public static string MimeTypeFor(string entryName) =>
        ContentTypeExtensions.FromExtension(Path.GetExtension(entryName)).ToMimeType();
}
