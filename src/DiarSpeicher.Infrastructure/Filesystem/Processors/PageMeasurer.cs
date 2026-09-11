using DiarSpeicher.Core.Filesystem;
using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public static class PageMeasurer
{
    public const int HeaderBytes = 64 * 1024;

    /// <summary>
    /// Lee como mucho <see cref="HeaderBytes"/> del stream y decodifica la cabecera.
    /// Devuelve nulos cuando el formato no se reconoce o la imagen esta corrupta: una
    /// pagina ilegible no debe tumbar el analisis del libro entero.
    /// </summary>
    public static async Task<(int? Width, int? Height)> MeasureAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[HeaderBytes];
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

    public static (int? Width, int? Height) Measure(ReadOnlySpan<byte> header)
    {
        try
        {
            using var data = SKData.CreateCopy(header);
            using var codec = SKCodec.Create(data);
            if (codec is null) return (null, null);

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0) return (null, null);

            return (info.Width, info.Height);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    public static string MimeTypeFor(string entryName) =>
        ContentTypeExtensions.FromExtension(Path.GetExtension(entryName)).ToMimeType();
}
