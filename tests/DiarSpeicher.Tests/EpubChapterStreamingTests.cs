using System.IO.Compression;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Dapper;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class EpubChapterStreamingTests : IDisposable
{
    private const string IntroChapter = """
        <html><body><h1>Prólogo</h1><p>Antes de todo.</p></body></html>
        """;

    private const string FirstChapter = """
        <html><body><h1>Uno</h1><p>Primer capítulo con algo más de texto.</p></body></html>
        """;

    private const string SecondChapter = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:xlink="http://www.w3.org/1999/xlink">
        <head><title>Dos</title><link rel="stylesheet" href="../Styles/book.css"/><style>p { color: red; }</style><script>alert(1)</script></head>
        <body class="calibre" onload="steal()">
        <h2 id="part-2" class="title">Parte dos</h2>
        <p style="color: black" onclick="steal()">Texto de la parte dos</p>
        <script>steal()</script>
        <img src="../Images/cover art.jpg" alt="Portada" srcset="../Images/big.jpg 2x"/>
        <img src="data:image/png;base64,AAAA" alt="Inline"/>
        <svg><image xlink:href="../Images/cover art.jpg"/></svg>
        <iframe src="https://evil.example"></iframe>
        <form><input name="x"/></form>
        <a href="https://example.org/autor">Autor</a>
        <a href="a-one.xhtml#top">Volver</a>
        <a href="javascript:steal()">Trampa</a>
        </body></html>
        """;

    private const string OwnerId = "user_admin";
    private static readonly byte[] CoverBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03];
    private static readonly DateTimeOffset FirstModification = new(2026, 3, 14, 9, 26, 53, TimeSpan.Zero);
    private static readonly AuthUser Owner = new() { Id = OwnerId, Username = "admin", IsServerOwner = true };
    private static readonly IHtmlParser Parser = new HtmlParser();

    private readonly TestSqliteDatabase _db = new();
    private readonly List<string> _files = [];
    private readonly DiarSpeicherService _service;
    private readonly string _seriesId = Guid.NewGuid().ToString();

    public EpubChapterStreamingTests()
    {
        _service = new DiarSpeicherService(
            _db.Factory,
            new CompositeBookProcessor([new EpubBookProcessor()]),
            new ScannerQueue(),
            NullLogger<DiarSpeicherService>.Instance,
            TestStorageOptions.Default(),
            TestReadingProgress.For(_db.Factory));
        SeedLibrary();
    }

    [Fact]
    public async Task Manifest_ListsChapterSizesInSpineOrder_AndPointsTheTocAtChaptersAndAnchors()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");

        var manifest = await _service.GetEpubManifestAsync(Owner, mediaId);

        Assert.NotNull(manifest);
        Assert.Equal(new[] { ByteCount(IntroChapter), ByteCount(FirstChapter), ByteCount(SecondChapter) }, manifest.ChapterSizes);
        Assert.Equal(
            new[] { ("Prólogo", 0, (string?)null), ("Uno", 1, null), ("Parte dos", 2, "ep-part-2") },
            manifest.Toc.Select(entry => (entry.Title, entry.Chapter, entry.Anchor)));
    }

    [Fact]
    public async Task Manifest_WithoutNcx_OffersOneSectionPerSpineChapter()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: false), "epub");

        var manifest = await _service.GetEpubManifestAsync(Owner, mediaId);

        Assert.NotNull(manifest);
        Assert.Equal(
            new[] { ("Sección 1", 0, (string?)null), ("Sección 2", 1, null), ("Sección 3", 2, null) },
            manifest.Toc.Select(entry => (entry.Title, entry.Chapter, entry.Anchor)));
    }

    [Fact]
    public async Task Chapter_DropsActiveContentAndBookStyling_AndKeepsTheText()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");

        var body = await ChapterBodyAsync(mediaId, 2);

        Assert.Empty(body.QuerySelectorAll("script, style, link, meta, iframe, form, input"));
        var attributeNames = body.QuerySelectorAll("*")
            .SelectMany(element => element.Attributes.Select(attribute => attribute.Name))
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(new[] { "alt", "href", "id", "rel", "src", "target" }, attributeNames);
        Assert.Equal(new[] { "ep-part-2" }, body.QuerySelectorAll("[id]").Select(element => element.Id));
        Assert.Equal(new[] { "Parte dos", "Texto de la parte dos" }, body.QuerySelectorAll("h2, p").Select(element => element.TextContent));
    }

    [Fact]
    public async Task Chapter_PointsImagesAtTheVersionedResourceEndpoint_AndLeavesInlineImagesAlone()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");
        var resourceBase = ResourceBase(mediaId);

        var body = await ChapterBodyAsync(mediaId, 2);

        var imageSources = body.QuerySelectorAll("img").Select(image => image.GetAttribute("src")).ToList();
        var svgImage = Assert.Single(body.QuerySelectorAll("image"));
        var version = VersionOf(imageSources[0]!);
        var coverUrl = $"{resourceBase}OEBPS/Images/cover%20art.jpg?v={version}";
        Assert.Equal(new[] { coverUrl, "data:image/png;base64,AAAA" }, imageSources);
        Assert.Equal(coverUrl, svgImage.GetAttribute("href"));
        Assert.Null(svgImage.GetAttribute("xlink:href"));
    }

    [Fact]
    public async Task Chapter_RewrittenImageUrl_ServesTheImageBytes()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");
        var resourceBase = ResourceBase(mediaId);
        var body = await ChapterBodyAsync(mediaId, 2);
        var source = body.QuerySelector("img")!.GetAttribute("src")!;

        var routedPath = Uri.UnescapeDataString(source[resourceBase.Length..source.IndexOf('?')]);
        var resource = await _service.GetEpubResourceAsync(Owner, mediaId, routedPath);

        Assert.NotNull(resource);
        Assert.Equal(CoverBytes, resource.Value.Data);
        Assert.Equal("image/jpeg", resource.Value.ContentType);
    }

    [Fact]
    public async Task Chapter_KeepsExternalLinksInANewTab_AndDisarmsTheRest()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");

        var body = await ChapterBodyAsync(mediaId, 2);

        Assert.Equal(
            new[]
            {
                ("Autor", (string?)"https://example.org/autor", (string?)"_blank", (string?)"noopener noreferrer"),
                ("Volver", null, null, null),
                ("Trampa", null, null, null),
            },
            body.QuerySelectorAll("a").Select(link => (link.TextContent, link.GetAttribute("href"), link.GetAttribute("target"), link.GetAttribute("rel"))));
    }

    [Fact]
    public async Task ImageVersion_ChangesWhenTheFileChanges_AndStaysPutOtherwise()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");

        var original = await ImageVersionAsync(mediaId);
        var unchanged = await ImageVersionAsync(mediaId);
        _db.Connection.Execute("""UPDATE "Media" SET "Size" = 2048 WHERE "Id" = @Id;""", new { Id = mediaId });
        var resized = await ImageVersionAsync(mediaId);
        _db.Connection.Execute("""UPDATE "Media" SET "ModifiedAt" = @ModifiedAt WHERE "Id" = @Id;""", new { Id = mediaId, ModifiedAt = FirstModification.AddHours(5) });
        var touched = await ImageVersionAsync(mediaId);

        Assert.Equal(original, unchanged);
        Assert.Equal(3, new[] { original, resized, touched }.Distinct().Count());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task Chapter_ExistsOnlyInsideTheSpine(int index, bool exists)
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "epub");

        var chapter = await _service.GetEpubChapterAsync(Owner, mediaId, index, ResourceBase(mediaId));

        Assert.Equal(exists, chapter is not null);
    }

    [Fact]
    public async Task ManifestAndChapter_RefuseMediaThatIsNotAnEpub()
    {
        var mediaId = SeedMedia(WriteEpub(includeNcx: true), "cbz");

        Assert.Null(await _service.GetEpubManifestAsync(Owner, mediaId));
        Assert.Null(await _service.GetEpubChapterAsync(Owner, mediaId, 0, ResourceBase(mediaId)));
    }

    [Fact]
    public async Task ManifestAndChapter_RefuseAnUnknownMedia()
    {
        SeedMedia(WriteEpub(includeNcx: true), "epub");
        var unknownId = Guid.NewGuid().ToString();

        Assert.Null(await _service.GetEpubManifestAsync(Owner, unknownId));
        Assert.Null(await _service.GetEpubChapterAsync(Owner, unknownId, 0, ResourceBase(unknownId)));
    }

    public void Dispose()
    {
        _db.Dispose();
        foreach (var file in _files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private static string ResourceBase(string mediaId) => $"/lib-prefix/api/v2/epub/{mediaId}/resource/";

    private static long ByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    private static string VersionOf(string url) => url[(url.IndexOf("?v=", StringComparison.Ordinal) + 3)..];

    private async Task<IElement> ChapterBodyAsync(string mediaId, int index)
    {
        var chapter = await _service.GetEpubChapterAsync(Owner, mediaId, index, ResourceBase(mediaId));
        Assert.NotNull(chapter);
        return Parser.ParseDocument($"<html><body>{chapter.Html}</body></html>").Body!;
    }

    private async Task<string> ImageVersionAsync(string mediaId)
    {
        var body = await ChapterBodyAsync(mediaId, 2);
        return VersionOf(body.QuerySelector("img")!.GetAttribute("src")!);
    }

    private void SeedLibrary()
    {
        var conn = _db.Connection;
        var now = DateTimeOffset.UtcNow;
        var libraryId = Guid.NewGuid().ToString();
        conn.Execute("""INSERT INTO "LibraryConfigs" ("Id") VALUES (1);""");
        conn.Execute("""
            INSERT INTO "Users" ("Id", "Username", "IsServerOwner", "CreatedAt")
            VALUES (@Id, 'admin', 1, @Now);
            """, new { Id = OwnerId, Now = now });
        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Novelas', '/books', 'Ready', 1, @Now);
            """, new { Id = libraryId, Now = now });
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Saga', '/books/saga', @LibraryId, 'Ready', @Now);
            """, new { Id = _seriesId, LibraryId = libraryId, Now = now });
    }

    private string SeedMedia(string path, string extension)
    {
        var mediaId = Guid.NewGuid().ToString();
        _db.Connection.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "Size", "SeriesId", "Status", "CreatedAt", "ModifiedAt")
            VALUES (@Id, 'Libro', @Path, @Extension, 3, 1024, @SeriesId, 'Ready', @CreatedAt, @ModifiedAt);
            """, new { Id = mediaId, Path = path, Extension = extension, SeriesId = _seriesId, CreatedAt = FirstModification.AddDays(-1), ModifiedAt = FirstModification });
        return mediaId;
    }

    private string WriteEpub(bool includeNcx)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diarspeicher_stream_{Guid.NewGuid():N}.epub");
        _files.Add(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteText(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);
        WriteText(zip, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <manifest>
                <item id="one" href="Text/a-one.xhtml" media-type="application/xhtml+xml"/>
                <item id="two" href="Text/m-two.xhtml" media-type="application/xhtml+xml"/>
                <item id="intro" href="Text/z-intro.xhtml" media-type="application/xhtml+xml"/>
                <item id="cover" href="Images/cover art.jpg" media-type="image/jpeg"/>
              </manifest>
              <spine>
                <itemref idref="intro"/>
                <itemref idref="one"/>
                <itemref idref="two"/>
              </spine>
            </package>
            """);
        if (includeNcx)
        {
            WriteText(zip, "OEBPS/toc.ncx", """
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                  <navMap>
                    <navPoint id="n1"><navLabel><text>Prólogo</text></navLabel><content src="Text/z-intro.xhtml"/></navPoint>
                    <navPoint id="n2"><navLabel><text>Uno</text></navLabel><content src="Text/a-one.xhtml"/></navPoint>
                    <navPoint id="n3"><navLabel><text>Parte dos</text></navLabel><content src="Text/m-two.xhtml#part-2"/></navPoint>
                    <navPoint id="n4"><navLabel><text>Fantasma</text></navLabel><content src="Text/missing.xhtml"/></navPoint>
                  </navMap>
                </ncx>
                """);
        }

        WriteText(zip, "OEBPS/Text/z-intro.xhtml", IntroChapter);
        WriteText(zip, "OEBPS/Text/a-one.xhtml", FirstChapter);
        WriteText(zip, "OEBPS/Text/m-two.xhtml", SecondChapter);
        using (var image = zip.CreateEntry("OEBPS/Images/cover art.jpg").Open())
        {
            image.Write(CoverBytes);
        }

        return path;
    }

    private static void WriteText(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
