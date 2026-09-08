using System.IO.Compression;
using System.Xml.Linq;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class OpdsFeedAndStreamingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _tempDir;
    private readonly string _testCbzPath;
    private string _kidBookId = null!;
    private string _adultBookId = null!;
    private string _hiddenLibId = null!;

    public OpdsFeedAndStreamingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_opds_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _testCbzPath = Path.Combine(_tempDir, "sample_comic.cbz");
        CreateSampleCbz(_testCbzPath);

        SeedTestData(context);
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

    private void SeedTestData(DiarSpeicherDbContext context)
    {
        var cfg1 = new LibraryConfig();
        var cfg2 = new LibraryConfig();
        context.LibraryConfigs.AddRange(cfg1, cfg2);
        context.SaveChanges();

        var mainLib = new Library { Name = "Comics Library", Path = "/data/comics", ConfigId = cfg1.Id };
        var hiddenLib = new Library { Name = "Secret Archives", Path = "/data/secret", ConfigId = cfg2.Id };
        context.Libraries.AddRange(mainLib, hiddenLib);
        context.SaveChanges();
        _hiddenLibId = hiddenLib.Id;

        var series = new Series
        {
            Name = "Marvel Adventures",
            Path = "/data/comics/marvel",
            LibraryId = mainLib.Id
        };
        context.Series.Add(series);
        context.SaveChanges();

        var kidBook = new Media
        {
            Name = "Spider-Man Kid",
            Path = _testCbzPath,
            Extension = "cbz",
            Pages = 3,
            Size = new FileInfo(_testCbzPath).Length,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "Spider-Man Kid #1",
                AgeRating = 10,
                Summary = "Family friendly adventure",
                Writers = "Stan Lee"
            }
        };

        var adultBook = new Media
        {
            Name = "Deadpool MAX",
            Path = _testCbzPath,
            Extension = "cbz",
            Pages = 3,
            Size = new FileInfo(_testCbzPath).Length,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "Deadpool MAX #1",
                AgeRating = 18,
                Summary = "Explicit violence and adult themes",
                Writers = "David Lapham"
            }
        };

        context.Media.AddRange(kidBook, adultBook);

        var testUser = new User
        {
            Id = "reader_1",
            Username = "reader_john"
        };
        context.Users.Add(testUser);

        context.SaveChanges();

        _kidBookId = kidBook.Id;
        _adultBookId = adultBook.Id;
    }

    [Fact]
    public async Task GetCatalogXmlAsync_ReturnsValidCatalogFeeds()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

        // 1. Usuario con restricción de edad 12 años
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

        // 2. Dueño del servidor: ve ambos libros
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
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

        var user = new AuthUser { Id = "reader_1", Username = "reader_john", IsServerOwner = false };

        // Solicitar página 0 con zero_based = true (debe leer página 1 en el CBZ)
        var (extractedPage, book) = await service.GetBookPageAsync(
            user,
            _kidBookId,
            pageNumber: 0,
            zeroBased: true,
            trackProgression: true);

        Assert.NotNull(book);
        Assert.NotNull(extractedPage);
        Assert.True(extractedPage.Data.Length > 0);
        Assert.Equal("image/jpeg", extractedPage.ContentType.MimeType());

        // Verificar que se haya registrado la sesión de lectura en la base de datos
        var session = await context.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == _kidBookId && s.UserId == "reader_1");

        Assert.NotNull(session);
        Assert.Equal(1, session.EndPage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
    }

    [Fact]
    public async Task GetMediaForDownloadAsync_PreventsUnauthorizedAccess()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };

        // Búsqueda por "Spider"
        var xml = await service.GetSearchFeedXmlAsync(user, "Spider", null);
        Assert.NotNull(xml);
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var titles = doc.Root?.Elements(atom + "entry")
            .Select(e => e.Element(atom + "title")?.Value)
            .ToList();

        Assert.NotNull(titles);
        Assert.Contains("Spider-Man Kid #1", titles);

        // Búsqueda vacía -> retorna feed vacío sin errores
        var emptyXml = await service.GetSearchFeedXmlAsync(user, "", null);
        var emptyDoc = XDocument.Parse(emptyXml);
        Assert.Empty(emptyDoc.Root?.Elements(atom + "entry") ?? []);
    }

    [Fact]
    public async Task GetKeepReadingFeedXmlAsync_ReturnsBooksWithActiveSessions()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

        // Crear una sesión de lectura activa para reader_1 en _kidBookId
        context.ReadingSessions.Add(new ReadingSession
        {
            UserId = "reader_1",
            MediaId = _kidBookId,
            StartPage = 1,
            EndPage = 2,
            Status = ReadingStatus.Reading,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

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
        using var context = new DiarSpeicherDbContext(_options);
        var processor = new CompositeBookProcessor([new ZipBookProcessor()]);
        var service = new OpdsService(context, processor, NullLogger<OpdsService>.Instance);

        var user = new AuthUser { Id = "reader_1", Username = "reader_john", IsServerOwner = true };

        var (data, contentType) = await service.GetBookThumbnailAsync(user, _kidBookId);

        Assert.NotNull(data);
        Assert.True(data.Length > 0);
        Assert.Equal("image/jpeg", contentType);
    }

    public void Dispose()
    {
        _connection.Dispose();
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
