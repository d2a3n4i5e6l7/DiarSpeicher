using DiarSpeicher.Core.Filesystem;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using SkiaSharp;
using PDFtoImage;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

/// <summary>
/// PdfPig extracts embedded images directly for scanned manga and comics with zero transcoding.
/// When a page is pure vector or text (such as light novels or ebooks), PDFtoImage (Google PDFium)
/// rasterizes the page into a WebP image, matching the behavior of Stump and Komga.
/// </summary>
public class PdfBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension) =>
        extension.TrimStart('.').Equals("pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ProcessedBook> AnalyzeBookAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default, bool measurePages = false, BookAnalysisOptions? options = null)
    {
        var analysis = options ?? BookAnalysisOptions.Default;
        var pageCount = 0;
        ExtractedMetadata? metadata = null;
        ExtractedPage? cover = null;

        try
        {
            using var document = PdfDocument.Open(path);
            pageCount = document.NumberOfPages;
            if (analysis.ReadEmbeddedMetadata)
            {
                metadata = ReadMetadata(document);
            }

            if (includeCover && pageCount > 0)
            {
                cover = ExtractLargestImage(document.GetPage(1), cancellationToken)
                    ?? RasterizePage(path, 1);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(new ProcessedBook { Pages = 0 });
        }

        return Task.FromResult(new ProcessedBook
        {
            Pages = pageCount,
            Metadata = metadata,
            Cover = cover
        });
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            if (pageNumber < 1 || pageNumber > document.NumberOfPages)
            {
                return Task.FromResult<ExtractedPage?>(null);
            }

            var image = ExtractLargestImage(document.GetPage(pageNumber), cancellationToken);
            if (image != null)
            {
                return Task.FromResult<ExtractedPage?>(image);
            }

            // Fallback: si la página no tiene imágenes incrustadas (novela o texto vectorial), rasterizamos con PDFium
            return Task.FromResult(RasterizePage(path, pageNumber));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult<ExtractedPage?>(null);
        }
    }

    private static ExtractedPage? RasterizePage(string path, int pageNumber)
    {
        try
        {
            if (!File.Exists(path) || pageNumber < 1) return null;

            using var stream = File.OpenRead(path);
            using var skBitmap = Conversion.ToImage(stream, page: pageNumber - 1);
            if (skBitmap == null) return null;

            using var image = SKImage.FromBitmap(skBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Webp, 85);
            if (data == null) return null;

            return new ExtractedPage(ContentType.Webp, data.ToArray());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A scanned page is normally one full-bleed image, but pages also carry logos and
    /// decorations. The largest image by pixel area is the page content in practice.
    /// </summary>
    private static ExtractedPage? ExtractLargestImage(Page page, CancellationToken cancellationToken)
    {
        ExtractedPage? best = null;
        var bestArea = 0L;

        foreach (var image in page.GetImages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetBytes(image, out var bytes, out var contentType))
            {
                continue;
            }

            var area = (long)image.WidthInSamples * image.HeightInSamples;
            if (area > bestArea)
            {
                bestArea = area;
                best = new ExtractedPage(contentType, bytes);
            }
        }

        return best;
    }

    /// <summary>
    /// JPEG-compressed images are stored verbatim inside the PDF and can be served as-is.
    /// Other encodings decode to raw samples that would need re-encoding, which PdfPig does
    /// not do, so those are skipped rather than served as an unreadable blob.
    /// </summary>
    private static bool TryGetBytes(IPdfImage image, out byte[] bytes, out ContentType contentType)
    {
        if (image.TryGetPng(out var png) && png != null)
        {
            bytes = png;
            contentType = ContentType.Png;
            return true;
        }

        var raw = image.RawBytes;
        if (IsJpeg(raw))
        {
            bytes = raw.ToArray();
            contentType = ContentType.Jpeg;
            return true;
        }

        bytes = [];
        contentType = ContentType.Unknown;
        return false;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> data) =>
        data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    private static ExtractedMetadata? ReadMetadata(PdfDocument document)
    {
        var info = document.Information;
        if (string.IsNullOrWhiteSpace(info.Title) &&
            string.IsNullOrWhiteSpace(info.Author) &&
            string.IsNullOrWhiteSpace(info.Subject))
        {
            return null;
        }

        return new ExtractedMetadata
        {
            Title = string.IsNullOrWhiteSpace(info.Title) ? null : info.Title,
            Writers = string.IsNullOrWhiteSpace(info.Author) ? null : info.Author,
            Summary = string.IsNullOrWhiteSpace(info.Subject) ? null : info.Subject
        };
    }
}
