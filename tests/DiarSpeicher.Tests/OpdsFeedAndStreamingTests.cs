using System.IO.Compression;
using System.Xml.Linq;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class OpdsFeedAndStreamingTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _tempDir;
    private readonly string _thumbnailPath;
    private readonly string _testCbzPath;
    private string _kidBookId = null!;
    private string _adultBookId = null!;
    private string _hiddenLibId = null!;

    public OpdsFeedAndStreamingTests()
    {
        _db = new TestSqliteDatabase();

        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_opds_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _testCbzPath = Path.Combine(_tempDir, "sample_comic.cbz");
        _thumbnailPath = Path.Combine(_tempDir, "sample_comic_thumb.jpg");
        File.WriteAllBytes(_thumbnailPath, JpegBuilder.Solid(64, 96));
        CreateSampleCbz(_testCbzPath);

        SeedTestData();
    }

    private static void CreateSampleCbz(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        // Dummy JPEG 1x1
        var dummyJpeg = new byte[]
        {
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
            0x00, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01,
            0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x37, 0xFF, 0xD9
        };

        var entry1 = archive.CreateEntry("01_cover.jpg");
        using (var s = entry1.Open()) s.Write(dummyJpeg);

        var entry2 = archive.CreateEntry("02_page.jpg");
        using (var s = entry2.Open()) s.Write(dummyJpeg);

        var entry3 = archive.CreateEntry("03_page.jpg");
        using (var s = entry3.Open()) s.Write(dummyJpeg);
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        conn.Execute("""
            INSERT INTO "LibraryConfigs" ("Id") VALUES (1);
            INSERT INTO "LibraryConfigs" ("Id") VALUES (2);
            """);

        var mainLibId = Guid.NewGuid().ToString();
        var hiddenLibId = Guid.NewGuid().ToString();
        _hiddenLibId = hiddenLibId;

        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@MainId, 'Comics Library', '/data/comics', 'Ready', 1, @Now);
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@HiddenId, 'Secret Archives', '/data/secret', 'Ready', 2, @Now);
            """, new { MainId = mainLibId, HiddenId = hiddenLibId, Now = DateTimeOffset.UtcNow });

        var seriesId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Marvel Adventures', '/data/comics/marvel', @LibId, 'Ready', @Now);
            """, new { Id = seriesId, LibId = mainLibId, Now = DateTimeOffset.UtcNow });

        var kidBookId = Guid.NewGuid().ToString();
        var adultBookId = Guid.NewGuid().ToString();
        _kidBookId = kidBookId;
        _adultBookId = adultBookId;

        var fileSize = new FileInfo(_testCbzPath).Length;
        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "Size", "SeriesId", "ThumbnailPath", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'cbz', 3, @Size, @SeriesId, @ThumbnailPath, 'Ready', @Now);
            """, new[]
        {
            new { Id = kidBookId, Name = "Spider-Man Kid", Path = _testCbzPath, Size = fileSize, SeriesId = seriesId, ThumbnailPath = _thumbnailPath, Now = DateTimeOffset.UtcNow },
            new { Id = adultBookId, Name = "Deadpool MAX", Path = _testCbzPath, Size = fileSize, SeriesId = seriesId, ThumbnailPath = (string?)null, Now = DateTimeOffset.UtcNow }
        });

        conn.Execute("""
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "AgeRating", "Summary", "Writers")
            VALUES (@MediaId, @Title, @AgeRating, @Summary, @Writers);
            """, new[]
        {
            new { MediaId = kidBookId, Title = "Spider-Man Kid #1", AgeRating = (int?)10, Summary = "Family friendly adventure", Writers = "Stan Lee" },
            new { MediaId = adultBookId, Title = "Deadpool MAX #1", AgeRating = (int?)18, Summary = "Explicit violence and adult themes", Writers = "David Lapham" }
        });

        conn.Execute("""
            INSERT INTO "Users" ("Id", "Username", "CreatedAt")
            VALUES ('reader_1', 'reader_john', @Now);
            """, new { Now = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task GetCatalogXmlAsync_ReturnsValidCatalogFeeds()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var xml = await service.GetCatalogXmlAsync(user, null);

        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var entryIds = doc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "id")?.Value)
            .ToList();

        Assert.NotNull(entryIds);
        Assert.Contains("keepReading", entryIds);
        Assert.Contains("allSeries", entryIds);
        Assert.Contains("allLibraries", entryIds);
        Assert.Contains("allBooks", entryIds);
        Assert.Contains("latestBooks", entryIds);
    }

    [Fact]
    public async Task GetBooksFeedAsync_EnforcesParentalAgeRestriction()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        // 1. Usuario con restriccion de edad 12 anos
        var childUser = new AuthUser
        {
            Id = "child_user",
            Username = "timmy",
            IsServerOwner = false,
            AgeRestriction = 12
        };

        var childXml = await service.GetBooksFeedAsync(childUser, null, 0, null);
        var childDoc = XDocument.Parse(childXml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var childBookTitles = childDoc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "title")?.Value)
            .ToList();

        Assert.NotNull(childBookTitles);
        Assert.Contains("Spider-Man Kid #1", childBookTitles);
        Assert.DoesNotContain("Deadpool MAX #1", childBookTitles);

        // 2. Dueno del servidor: ve ambos libros
        var ownerUser = new AuthUser { Id = "owner", Username = "boss", IsServerOwner = true };
        var ownerXml = await service.GetBooksFeedAsync(ownerUser, null, 0, null);
        var ownerDoc = XDocument.Parse(ownerXml);

        var ownerBookTitles = ownerDoc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "title")?.Value)
            .ToList();

        Assert.NotNull(ownerBookTitles);
        Assert.Contains("Spider-Man Kid #1", ownerBookTitles);
        Assert.Contains("Deadpool MAX #1", ownerBookTitles);
    }

    [Fact]
    public async Task GetLibrariesFeedAsync_EnforcesExcludedLibraries()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var restrictedUser = new AuthUser
        {
            Id = "user_restricted",
            Username = "alice",
            IsServerOwner = false,
            ExcludedLibraryIds = [_hiddenLibId]
        };

        var xml = await service.GetLibrariesFeedAsync(restrictedUser, null, null);
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var libNames = doc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "title")?.Value)
            .ToList();

        Assert.NotNull(libNames);
        Assert.Contains("Comics Library", libNames);
        Assert.DoesNotContain("Secret Archives", libNames);
    }

    [Fact]
    public async Task GetBookPageAsync_ExtractsRealPageAndTracksReadingProgression()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "reader_1", Username = "reader_john", IsServerOwner = false };

        var (extractedPage, book) = await service.OpenBookPageAsync(
            user,
            _kidBookId,
            pageNumber: 1,
            PageRequestKind.Reading);

        Assert.NotNull(book);
        Assert.NotNull(extractedPage);
        Assert.Equal("image/jpeg", extractedPage.ContentType.MimeType());

        await using (extractedPage.Content)
        {
            using var copia = new MemoryStream();
            await extractedPage.Content.CopyToAsync(copia);
            Assert.True(copia.Length > 0);
        }

        // Verificar que se haya registrado la sesion de lectura en la base de datos
        var session = await _db.Connection.QueryFirstOrDefaultAsync<ReadingSession>(
            """SELECT * FROM "ReadingSessions" WHERE "MediaId" = @MediaId AND "UserId" = @UserId""",
            new { MediaId = _kidBookId, UserId = "reader_1" });

        Assert.NotNull(session);
        Assert.Equal(1, session.EndPage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
    }

    [Fact]
    public async Task GetMediaForDownloadAsync_PreventsUnauthorizedAccess()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var childUser = new AuthUser
        {
            Id = "child_user",
            Username = "timmy",
            IsServerOwner = false,
            AgeRestriction = 12
        };

        // Libro permitido
        var allowedMedia = await service.GetMediaForDownloadAsync(childUser, _kidBookId);
        Assert.NotNull(allowedMedia);
        Assert.Equal(_kidBookId, allowedMedia.Id);

        // Libro prohibido (Deadpool MAX, age 18)
        var forbiddenMedia = await service.GetMediaForDownloadAsync(childUser, _adultBookId);
        Assert.Null(forbiddenMedia);
    }

    [Fact]
    public async Task GetSearchFeedXmlAsync_ReturnsLibrariesSeriesAndBooksMatchingQuery()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };

        // Busqueda por "Spider"
        var xml = await service.GetSearchFeedXmlAsync(user, "Spider", null);
        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var titles = doc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "title")?.Value)
            .ToList();

        Assert.NotNull(titles);
        Assert.Contains("Spider-Man Kid #1", titles);

        // Busqueda vacia -> retorna feed vacio sin errores
        var emptyXml = await service.GetSearchFeedXmlAsync(user, "", null);
        var emptyDoc = XDocument.Parse(emptyXml);
        Assert.Empty(emptyDoc.Root?.Elements(atom + "entry") ?? []);
    }

    [Fact]
    public async Task GetKeepReadingFeedXmlAsync_ReturnsBooksWithActiveSessions()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        // Crear una sesion de lectura activa para reader_1 en _kidBookId
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "ReadingSessions" ("UserId", "MediaId", "StartPage", "EndPage", "Status", "SessionDate", "CreatedAt", "UpdatedAt")
            VALUES (@UserId, @MediaId, @StartPage, @EndPage, @Status, @Now, @Now, @Now);
            """, new
        {
            UserId = "reader_1",
            MediaId = _kidBookId,
            StartPage = 1,
            EndPage = 2,
            Status = nameof(ReadingStatus.Reading),
            Now = DateTimeOffset.UtcNow
        });

        var user = new AuthUser { Id = "reader_1", Username = "reader_john", IsServerOwner = false };
        var xml = await service.GetKeepReadingFeedXmlAsync(user, null);

        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var titles = doc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "title")?.Value)
            .ToList();

        Assert.NotNull(titles);
        Assert.Contains("Spider-Man Kid #1", titles);
    }

    [Fact]
    public async Task GetBookThumbnailAsync_ReturnsValidImageData()
    {
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(_db.Factory, processor, NullLogger<OpdsService>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "reader_1", Username = "reader_john", IsServerOwner = true };

        var (data, contentType) = await service.GetBookThumbnailAsync(user, _kidBookId);

        Assert.NotNull(data);
        Assert.True(data.Length > 0);
        Assert.Equal("image/jpeg", contentType);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Cleanup best-effort
            }
        }
    }
}
