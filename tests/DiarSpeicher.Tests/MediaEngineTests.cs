using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace DiarSpeicher.Tests;

/// <summary>
/// Acceptance criteria of block B3: a PDF is indexed with its real page count instead of
/// zero, and thumbnails are served resized and re-encoded rather than as extracted.
/// </summary>
public sealed class MediaEngineTests : IDisposable
{
    private readonly string _workDir = Directory.CreateTempSubdirectory("diar-media-tests-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PdfProcessor_ClaimsOnlyPdf()
    {
        var processor = new PdfBookProcessor();

        Assert.True(processor.CanProcess("pdf"));
        Assert.True(processor.CanProcess(".PDF"));
        Assert.False(processor.CanProcess("cbz"));
        Assert.False(processor.CanProcess("epub"));
    }

    [Fact]
    public async Task PdfProcessor_ReportsTheRealPageCount()
    {
        var path = Path.Combine(_workDir, "libro.pdf");
        await File.WriteAllBytesAsync(path, PdfBuilder.WithPages(3));

        var result = await new PdfBookProcessor().AnalyzeBookAsync(path);

        Assert.Equal(3, result.Pages);
    }

    [Fact]
    public async Task PdfProcessor_OnACorruptFile_DoesNotThrow()
    {
        var path = Path.Combine(_workDir, "roto.pdf");
        await File.WriteAllTextAsync(path, "esto no es un PDF");

        var result = await new PdfBookProcessor().AnalyzeBookAsync(path, includeCover: true);

        Assert.Equal(0, result.Pages);
        Assert.Null(result.Cover);
    }

    [Fact]
    public async Task PdfIsRegisteredAsAnAcceptedExtension()
    {
        Assert.Contains("pdf", PathUtils.AcceptedMediaExtensions);

        var path = Path.Combine(_workDir, "compuesto.pdf");
        await File.WriteAllBytesAsync(path, PdfBuilder.WithPages(2));

        var composite = new CompositeBookProcessor(new IBookProcessor[]
        {
            new ZipBookProcessor(),
            new EpubBookProcessor(),
            new PdfBookProcessor()
        });

        var result = await composite.AnalyzeAsync(path);

        Assert.Equal(2, result.Pages);
    }

    [Fact]
    public async Task Thumbnail_IsResizedAndReEncodedToWebp()
    {
        var service = new ThumbnailService(
            new CompositeBookProcessor([]),
            NullLogger<ThumbnailService>.Instance,
            TestStorageOptions.Default());

        var cover = new ExtractedPage(ContentType.Png, PngBuilder.Solid(2000, 3000));

        var saved = await service.SaveThumbnailAsync("media-1", cover, _workDir);

        Assert.NotNull(saved);
        Assert.EndsWith(".webp", saved, StringComparison.Ordinal);

        using var image = SKBitmap.Decode(saved!);
        Assert.Equal(512, image.Width);
        Assert.Equal(768, image.Height);

        var thumbnailBytes = new FileInfo(saved!).Length;
        Assert.True(
            thumbnailBytes < cover.Data.Length / 10,
            $"The thumbnail ({thumbnailBytes} B) should be an order of magnitude smaller than the cover ({cover.Data.Length} B)");
    }

    [Fact]
    public async Task Thumbnail_DoesNotUpscaleASmallCover()
    {
        var service = new ThumbnailService(
            new CompositeBookProcessor([]),
            NullLogger<ThumbnailService>.Instance,
            TestStorageOptions.Default());

        var saved = await service.SaveThumbnailAsync("media-2", new ExtractedPage(ContentType.Png, PngBuilder.Solid(120, 180)), _workDir);

        using var image = SKBitmap.Decode(saved!);
        Assert.Equal(120, image.Width);
        Assert.Equal(180, image.Height);
    }

    [Fact]
    public async Task Thumbnail_WithAnUndecodableCover_KeepsTheOriginalBytes()
    {
        var service = new ThumbnailService(
            new CompositeBookProcessor([]),
            NullLogger<ThumbnailService>.Instance,
            TestStorageOptions.Default());

        var bytes = "no soy una imagen"u8.ToArray();

        var saved = await service.SaveThumbnailAsync("media-3", new ExtractedPage(ContentType.Jpeg, bytes), _workDir);

        Assert.NotNull(saved);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(saved!));
    }
}
