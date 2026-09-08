using System.IO.Compression;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using DiarSpeicher.Infrastructure.StumpV2;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class UploadAndPageExtractionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnection _connection;

    public UploadAndPageExtractionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DiarSpeicherUploadTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
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
            catch (Exception)
            {
                // Limpieza de directorio temporal con mejor esfuerzo
            }
        }

        GC.SuppressFinalize(this);
    }

    private DiarSpeicherDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new DiarSpeicherDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static byte[] CreateSampleCbz(string title, int pageCount)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var info = zip.CreateEntry("ComicInfo.xml");
            using (var s = info.Open())
            {
                var xml = $"<ComicInfo><Title>{title}</Title><PageCount>{pageCount}</PageCount></ComicInfo>";
                var b = System.Text.Encoding.UTF8.GetBytes(xml);
                s.Write(b, 0, b.Length);
            }

            for (int i = 1; i <= pageCount; i++)
            {
                var page = zip.CreateEntry($"{i:D3}.jpg");
                using var s = page.Open();
                s.Write([0xFF, 0xD8, 0xFF, 0xE0, (byte)i, 0xAA, 0xBB]);
            }
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task CreateLibraryAsync_CreatesDirectoryAndDatabaseRow()
    {
        using var db = CreateInMemoryDbContext();
        var processor = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(db, processor, queue, NullLogger<StumpV2Service>.Instance);

        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var libPath = Path.Combine(_tempDir, "NewLib");

        var result = await service.CreateLibraryAsync(owner, new StumpCreateLibraryInput
        {
            Name = "Manga",
            Path = libPath,
            Description = "Manga collection"
        });

        Assert.NotNull(result);
        Assert.True(Directory.Exists(libPath));
        Assert.Equal("Manga", result.Name);

        var dbLib = await db.Libraries.FirstOrDefaultAsync(l => l.Id == result.Id);
        Assert.NotNull(dbLib);
        Assert.Equal("Manga collection", dbLib.Description);
    }

    [Fact]
    public async Task UploadToLibraryAsync_PreventsPathTraversalAttacks()
    {
        using var db = CreateInMemoryDbContext();
        var processor = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(db, processor, queue, NullLogger<StumpV2Service>.Instance);

        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var libDir = Path.Combine(_tempDir, "SafeLib");
        Directory.CreateDirectory(libDir);

        var library = new Library
        {
            Id = Guid.NewGuid().ToString(),
            Name = "SafeLib",
            Path = libDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig()
        };
        db.Libraries.Add(library);
        await db.SaveChangesAsync();

        var cbzBytes = CreateSampleCbz("Malicious", 1);
        using var cbzStream = new MemoryStream(cbzBytes);

        var uploadInput = new[]
        {
            new StumpUploadFileInput { FileName = "hack.cbz", Content = cbzStream }
        };

        var result = await service.UploadToLibraryAsync(owner, library.Id, "../../../evil", uploadInput);
        Assert.Null(result);
    }

    [Fact]
    public async Task Upload_EndToEnd_UploadsCbz_ScansLibrary_And_FetchesFirstPage()
    {
        // 1. Arrange: In-memory DB, library directory and services
        using var db = CreateInMemoryDbContext();
        var libDir = Path.Combine(_tempDir, "MyComics");
        Directory.CreateDirectory(libDir);

        var libraryId = Guid.NewGuid().ToString();
        var library = new Library
        {
            Id = libraryId,
            Name = "MyComics",
            Path = libDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig { LibraryPattern = LibraryPattern.SeriesBased }
        };
        db.Libraries.Add(library);
        await db.SaveChangesAsync();

        var compositeProcessor = new CompositeBookProcessor(new IBookProcessor[]
        {
            new ZipBookProcessor(),
            new RarBookProcessor(),
            new EpubBookProcessor()
        });
        var queue = new ScannerQueue();
        var v2Service = new StumpV2Service(db, compositeProcessor, queue, NullLogger<StumpV2Service>.Instance);

        var scannerService = new LibraryScannerService(
            db,
            new DirectoryScanner(),
            compositeProcessor,
            new ThumbnailService(compositeProcessor, NullLogger<ThumbnailService>.Instance),
            NullLogger<LibraryScannerService>.Instance);

        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };

        // 2. Act: Upload a real CBZ comic with 3 pages into a "Batman" series subfolder
        var cbzBytes = CreateSampleCbz("Batman Year One", 3);
        using var cbzStream = new MemoryStream(cbzBytes);

        var uploadResult = await v2Service.UploadToLibraryAsync(
            owner,
            libraryId,
            subpath: "Batman",
            files: new[]
            {
                new StumpUploadFileInput { FileName = "Batman_01.cbz", Content = cbzStream }
            });

        Assert.NotNull(uploadResult);
        Assert.Equal(1, uploadResult.UploadedCount);
        Assert.True(uploadResult.ScanJobTriggered);

        var expectedFile = Path.Combine(libDir, "Batman", "Batman_01.cbz");
        Assert.True(File.Exists(expectedFile));

        // 3. Scan the library (as the background worker would do)
        var scanReport = await scannerService.ScanLibraryAsync(libraryId);
        Assert.True(scanReport.Success);
        Assert.Equal(1UL, scanReport.CreatedSeries);
        Assert.Equal(1UL, scanReport.CreatedMedia);

        // 4. Verify Media was persisted with correct pages and metadata
        var media = await db.Media.Include(m => m.Metadata).FirstOrDefaultAsync(m => m.Name == "Batman Year One");
        Assert.NotNull(media);
        Assert.Equal(3, media.Pages);
        Assert.Equal("cbz", media.Extension);

        // 5. Fetch the first page via Stump v2 Service!
        var page1 = await v2Service.GetMediaPageAsync(owner, media.Id, 1);
        Assert.NotNull(page1);
        Assert.Equal(ContentType.Jpeg, page1.ContentType);
        Assert.Equal("image/jpeg", page1.ContentType.ToMimeType());
        Assert.NotEmpty(page1.Data);
        // Verify JPEG magic bytes FF D8 FF E0
        Assert.Equal(0xFF, page1.Data[0]);
        Assert.Equal(0xD8, page1.Data[1]);
        Assert.Equal(0xFF, page1.Data[2]);
        Assert.Equal(0xE0, page1.Data[3]);
    }
}
