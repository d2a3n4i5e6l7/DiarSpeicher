using DiarSpeicher.Infrastructure.Filesystem.Processors;
using SkiaSharp;

namespace DiarSpeicher.Tests;

public class ImageHeaderTests
{
    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void LeeLasDimensionesDeUnaImagenReal(SKEncodedImageFormat format)
    {
        var bytes = Encode(1234, 567, format);

        Assert.True(ImageHeader.TryRead(bytes, out var width, out var height));
        Assert.Equal(1234, width);
        Assert.Equal(567, height);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void CoincideConLoQueDiceSkia(SKEncodedImageFormat format)
    {
        var bytes = Encode(800, 1280, format);

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);

        Assert.True(ImageHeader.TryRead(bytes, out var width, out var height));
        Assert.Equal(codec!.Info.Width, width);
        Assert.Equal(codec.Info.Height, height);
    }

    [Fact]
    public void UnaPaginaDobleMuyAnchaNoSeTrunca()
    {
        var bytes = Encode(4096, 1600, SKEncodedImageFormat.Png);

        Assert.True(ImageHeader.TryRead(bytes, out var width, out var height));
        Assert.Equal(4096, width);
        Assert.Equal(1600, height);
    }

    [Fact]
    public void ElJpegSeEncuentraAunConUnBloqueExifDelante()
    {
        var jpeg = Encode(640, 480, SKEncodedImageFormat.Jpeg);

        var exif = new byte[2 + 2 + 8192];
        exif[0] = 0xFF;
        exif[1] = 0xE1;
        exif[2] = (byte)((exif.Length - 2) >> 8);
        exif[3] = (byte)((exif.Length - 2) & 0xFF);

        var conExif = new byte[2 + exif.Length + (jpeg.Length - 2)];
        conExif[0] = 0xFF;
        conExif[1] = 0xD8;
        exif.CopyTo(conExif, 2);
        jpeg.AsSpan(2).CopyTo(conExif.AsSpan(2 + exif.Length));

        Assert.True(ImageHeader.TryRead(conExif, out var width, out var height));
        Assert.Equal(640, width);
        Assert.Equal(480, height);
    }

    [Theory]
    [InlineData("R0lGODdhZQLfAYcAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", 613, 479)]
    [InlineData("R0lGODdhAQABAIEAADNmzAAAAAAAAAAAACwAAAAAAQA=", 1, 1)]
    [InlineData("R0lGODdhABBABocAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", 4096, 1600)]
    public void UnGifDeUnCodificadorRealSeLeeBien(string cabecera, int esperadoAncho, int esperadoAlto)
    {
        Assert.True(ImageHeader.TryRead(Convert.FromBase64String(cabecera), out var width, out var height));
        Assert.Equal(esperadoAncho, width);
        Assert.Equal(esperadoAlto, height);
    }

    [Theory]
    [InlineData("Qk3ulQAAAAAAAD4AAAAoAAAAZQIAAN8BAAABAAEAAAA=", 613, 479)]
    [InlineData("Qk1CAAAAAAAAAD4AAAAoAAAAAQAAAAEAAAABAAEAAAA=", 1, 1)]
    [InlineData("Qk2esxAAAAAAAD4AAAAoAAAAsAkAALQNAAABAAEAAAA=", 2480, 3508)]
    public void UnBmpDeUnCodificadorRealSeLeeBien(string cabecera, int esperadoAncho, int esperadoAlto)
    {
        Assert.True(ImageHeader.TryRead(Convert.FromBase64String(cabecera), out var width, out var height));
        Assert.Equal(esperadoAncho, width);
        Assert.Equal(esperadoAlto, height);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 })]
    public void LoQueNoReconoceLoDejaPasarAlRespaldo(byte[] basura)
    {
        Assert.False(ImageHeader.TryRead(basura, out _, out _));
    }

    [Fact]
    public void UnJpegCortadoNoRevienta()
    {
        var jpeg = Encode(300, 400, SKEncodedImageFormat.Jpeg);

        for (var corte = 2; corte < Math.Min(jpeg.Length, 40); corte++)
        {
            ImageHeader.TryRead(jpeg.AsSpan(0, corte), out _, out _);
        }
    }

    [Fact]
    public void PageMeasurerDevuelveLoMismoQueAntes()
    {
        var bytes = Encode(1200, 1920, SKEncodedImageFormat.Webp);

        var (width, height) = PageMeasurer.Measure(bytes);

        Assert.Equal(1200, width);
        Assert.Equal(1920, height);
    }
}
