using System.IO.Compression;
using System.Text;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public class MediaProcessingTests : IDisposable
{
    private readonly string _tempDir;

    public MediaProcessingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "diarspeicher_mediatests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NaturalSortComparer_SortsAlphanumericFilenamesCorrectly()
    {
        var input = new List<string> { "page10.jpg", "page1.jpg", "page2.jpg", "page01.jpg", "page20.jpg", "page02.jpg" };

        input.Sort(NaturalSortComparer.OrdinalIgnoreCase);

        Assert.Equal("page1.jpg", input[0]);
        Assert.Equal("page01.jpg", input[1]);
        Assert.Equal("page2.jpg", input[2]);
        Assert.Equal("page02.jpg", input[3]);
        Assert.Equal("page10.jpg", input[4]);
        Assert.Equal("page20.jpg", input[5]);
    }

    [Fact]
    public void MediaHasher_GeneratesStumpAndKoreaderHashes()
    {
        var filePath = Path.Combine(_tempDir, "sample_book.bin");
        var content = new byte[50000]; // Larger than 40KB to exercise sample offsets
        for (int i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(i % 256);
        }
        File.WriteAllBytes(filePath, content);

        var stumpHash = MediaHasher.ComputeStumpHash(filePath, content.Length);
        var koreaderHash = MediaHasher.ComputeKoreaderHash(filePath);

        Assert.NotNull(stumpHash);
        Assert.Equal(64, stumpHash.Length); // SHA-256 hex string is 64 chars

        Assert.NotNull(koreaderHash);
        Assert.Equal(32, koreaderHash.Length); // MD5 hex string is 32 chars
    }

    [Fact]
    public void ComicInfoParser_ParsesAllMetadataAndNormalizesAgeRating()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ComicInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <Title>Eclipse of the Moon</Title>
              <Series>Berserk</Series>
              <Number>12.5</Number>
              <Volume>13</Volume>
              <Summary>The fateful eclipse begins.</Summary>
              <AgeRating>Adults Only 18+</AgeRating>
              <Genre>Dark Fantasy</Genre>
              <Tags>Horror, Seinen, Demons</Tags>
              <Year>1996</Year>
              <Month>8</Month>
              <Day>20</Day>
              <Writer>Kentaro Miura</Writer>
              <Penciller>Kentaro Miura</Penciller>
            </ComicInfo>
            """;

        var (meta, tags) = ComicInfoParser.Parse(xml);

        Assert.Equal("Eclipse of the Moon", meta.Title);
        Assert.Equal("Berserk", meta.Series);
        Assert.Equal(12.5, meta.Number);
        Assert.Equal(13, meta.Volume);
        Assert.Equal("The fateful eclipse begins.", meta.Summary);
        Assert.Equal(18, meta.AgeRating);
        Assert.Equal("Dark Fantasy", meta.Genre);
        Assert.Equal(1996, meta.Year);
        Assert.Equal(8, meta.Month);
        Assert.Equal(20, meta.Day);
        Assert.Equal("Kentaro Miura", meta.Writers);
        Assert.Equal("Kentaro Miura", meta.Pencillers);

        Assert.Equal(3, tags.Count);
        Assert.Contains("Horror", tags);
        Assert.Contains("Seinen", tags);
        Assert.Contains("Demons", tags);
    }

    [Fact]
    public async Task ZipBookProcessor_ExtractsComicInfo_Pages_And_IndividualPage()
    {
        var cbzPath = Path.Combine(_tempDir, "manga_ch01.cbz");

        // Create a realistic CBZ (zip archive)
        using (var zip = ZipFile.Open(cbzPath, ZipArchiveMode.Create))
        {
            var comicInfoEntry = zip.CreateEntry("ComicInfo.xml");
            using (var stream = comicInfoEntry.Open())
            {
                var xml = """
                    <ComicInfo>
                      <Title>Chapter 1: The Black Swordsman</Title>
                      <Series>Berserk</Series>
                      <Number>1</Number>
                      <AgeRating>Mature 17+</AgeRating>
                      <Tags>Action, Dark Fantasy</Tags>
                    </ComicInfo>
                    """;
                var bytes = Encoding.UTF8.GetBytes(xml);
                stream.Write(bytes, 0, bytes.Length);
            }

            // Create 3 page images
            var page1 = zip.CreateEntry("01_cover.jpg");
            using (var stream = page1.Open()) stream.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02]);

            var page2 = zip.CreateEntry("02_page.png");
            using (var stream = page2.Open()) stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]);

            var page3 = zip.CreateEntry("03_page.jpg");
            using (var stream = page3.Open()) stream.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x03, 0x04]);
        }

        var processor = new ZipBookProcessor();
        Assert.True(processor.CanProcess("cbz"));
        Assert.True(processor.CanProcess("zip"));

        // Analyze
        var analysis = await processor.AnalyzeBookAsync(cbzPath);

        Assert.Equal(3, analysis.Pages);
        Assert.NotNull(analysis.Hash);
        Assert.NotNull(analysis.Metadata);
        Assert.Equal("Chapter 1: The Black Swordsman", analysis.Metadata.Title);
        Assert.Equal(17, analysis.Metadata.AgeRating);
        Assert.Equal(2, analysis.Tags.Count);

        // Extract Page 1
        var page1Extracted = await processor.ExtractPageAsync(cbzPath, 1);
        Assert.NotNull(page1Extracted);
        Assert.Equal(ContentType.Jpeg, page1Extracted.ContentType);
        Assert.Equal(6, page1Extracted.Data.Length);
        Assert.Equal(0xFF, page1Extracted.Data[0]);

        // Extract Page 2
        var page2Extracted = await processor.ExtractPageAsync(cbzPath, 2);
        Assert.NotNull(page2Extracted);
        Assert.Equal(ContentType.Png, page2Extracted.ContentType);

        // Out of bounds page
        var pageNotFound = await processor.ExtractPageAsync(cbzPath, 99);
        Assert.Null(pageNotFound);
    }

    [Fact]
    public async Task EpubBookProcessor_ExtractsOpfMetadata_And_CoverPage()
    {
        var epubPath = Path.Combine(_tempDir, "sample_book.epub");

        // Create a realistic EPUB
        using (var zip = ZipFile.Open(epubPath, ZipArchiveMode.Create))
        {
            // META-INF/container.xml
            var container = zip.CreateEntry("META-INF/container.xml");
            using (var s = container.Open())
            {
                var containerXml = """
                    <?xml version="1.0"?>
                    <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                      <rootfiles>
                        <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                      </rootfiles>
                    </container>
                    """;
                s.Write(Encoding.UTF8.GetBytes(containerXml));
            }

            // OEBPS/content.opf
            var opf = zip.CreateEntry("OEBPS/content.opf");
            using (var s = opf.Open())
            {
                var opfXml = """
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                      <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                        <dc:title>Dune</dc:title>
                        <dc:creator>Frank Herbert</dc:creator>
                        <dc:description>Arrakis, the spice planet.</dc:description>
                        <dc:date>1965-08-01</dc:date>
                        <dc:subject>Sci-Fi</dc:subject>
                        <meta name="cover" content="cover-image-id"/>
                      </metadata>
                      <manifest>
                        <item id="cover-image-id" href="images/cover.jpg" media-type="image/jpeg" properties="cover-image"/>
                        <item id="chap1" href="chap1.xhtml" media-type="application/xhtml+xml"/>
                        <item id="chap2" href="chap2.xhtml" media-type="application/xhtml+xml"/>
                      </manifest>
                      <spine>
                        <itemref idref="chap1"/>
                        <itemref idref="chap2"/>
                      </spine>
                    </package>
                    """;
                s.Write(Encoding.UTF8.GetBytes(opfXml));
            }

            // OEBPS/images/cover.jpg
            var cover = zip.CreateEntry("OEBPS/images/cover.jpg");
            using (var s = cover.Open())
            {
                s.Write([0xFF, 0xD8, 0xFF, 0xEE, 0x42]);
            }

            // Chapters
            var chap1 = zip.CreateEntry("OEBPS/chap1.xhtml");
            using (var s = chap1.Open()) s.Write(Encoding.UTF8.GetBytes("<html><body>Chapter 1</body></html>"));

            var chap2 = zip.CreateEntry("OEBPS/chap2.xhtml");
            using (var s = chap2.Open()) s.Write(Encoding.UTF8.GetBytes("<html><body>Chapter 2</body></html>"));
        }

        var processor = new EpubBookProcessor();
        Assert.True(processor.CanProcess("epub"));

        // Analyze
        var analysis = await processor.AnalyzeBookAsync(epubPath);

        Assert.Equal("Dune", analysis.Metadata?.Title);
        Assert.Equal("Frank Herbert", analysis.Metadata?.Writers);
        Assert.Equal("Arrakis, the spice planet.", analysis.Metadata?.Summary);
        Assert.Equal(1965, analysis.Metadata?.Year);
        Assert.Equal(2, analysis.Pages);
        Assert.NotNull(analysis.Hash);
        Assert.NotNull(analysis.KoreaderHash);
        Assert.Contains("Sci-Fi", analysis.Tags);

        // Extract Page 1 (Cover)
        var coverPage = await processor.ExtractPageAsync(epubPath, 1);
        Assert.NotNull(coverPage);
        Assert.Equal(ContentType.Jpeg, coverPage.ContentType);
        Assert.Equal(5, coverPage.Data.Length);
        Assert.Equal(0x42, coverPage.Data[4]);
    }

    [Fact]
    public async Task ThumbnailService_ExtractsAndSavesCoverImageToDisk()
    {
        var cbzPath = Path.Combine(_tempDir, "solo_leveling_v01.cbz");
        using (var zip = ZipFile.Open(cbzPath, ZipArchiveMode.Create))
        {
            var cover = zip.CreateEntry("00_cover.jpg");
            using var s = cover.Open();
            s.Write([0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0x33]);
        }

        var composite = new CompositeBookProcessor([new ZipBookProcessor(), new EpubBookProcessor()]);
        var thumbService = new ThumbnailService(composite, NullLogger<ThumbnailService>.Instance);

        var thumbsDir = Path.Combine(_tempDir, "thumbnails");
        var mediaId = "media_12345";

        var savedPath = await thumbService.GenerateThumbnailAsync(mediaId, cbzPath, thumbsDir);

        Assert.NotNull(savedPath);
        Assert.True(File.Exists(savedPath));
        Assert.Equal(Path.Combine(thumbsDir, "media_12345.jpg"), savedPath);

        var bytes = await File.ReadAllBytesAsync(savedPath);
        Assert.Equal(7, bytes.Length);
        Assert.Equal(0x33, bytes[6]);
    }
}
