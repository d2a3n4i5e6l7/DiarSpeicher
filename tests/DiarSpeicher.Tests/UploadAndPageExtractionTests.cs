using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class UploadAndPageExtractionTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _tempDir;

    public UploadAndPageExtractionTests()
    {
        _db = new TestSqliteDatabase();
        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_upload_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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
            catch (Exception)
            {
                // Limpieza de directorio temporal con mejor esfuerzo
            }
        }

        GC.SuppressFinalize(this);
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
        var processor = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(
            _db.Factory, processor, queue, NullLogger<DiarSpeicherService>.Instance,
            TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory), TestLibraryRoots.Allowing(_tempDir));

        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var libPath = Path.Combine(_tempDir, "NewLib");

        var result = await service.CreateLibraryAsync(owner, new DiarSpeicherCreateLibraryInput
        {
            Name = "Manga",
            Path = libPath,
            Description = "Manga collection"
        });

        Assert.NotNull(result);
        Assert.True(Directory.Exists(libPath));
        Assert.Equal("Manga", result.Name);

        var dbLib = await _db.Connection.QueryFirstOrDefaultAsync<Library>(
            """SELECT * FROM "Libraries" WHERE "Id" = @Id""", new { result.Id });
        Assert.NotNull(dbLib);
        Assert.Equal("Manga collection", dbLib.Description);
    }

    [Fact]
    public async Task UploadToLibraryAsync_PreventsPathTraversalAttacks()
    {
        var processor = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(_db.Factory, processor, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var libDir = Path.Combine(_tempDir, "SafeLib");
        Directory.CreateDirectory(libDir);

        var configId = await _db.Connection.QuerySingleAsync<long>("""INSERT INTO "LibraryConfigs" DEFAULT VALUES RETURNING "Id";""");

        var libraryId = Guid.NewGuid().ToString();
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'SafeLib', @Path, 'Ready', @ConfigId, @Now);
            """, new { Id = libraryId, Path = libDir, ConfigId = configId, Now = DateTimeOffset.UtcNow });

        var cbzBytes = CreateSampleCbz("Malicious", 1);
        using var cbzStream = new MemoryStream(cbzBytes);

        var uploadInput = new[]
        {
            new DiarSpeicherUploadFileInput { FileName = "hack.cbz", Content = cbzStream }
        };

        var result = await service.UploadToLibraryAsync(owner, libraryId, "../../../evil", uploadInput);
        Assert.Equal(UploadOutcome.LibraryNotFound, result.Outcome);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Upload_EndToEnd_UploadsCbz_ScansLibrary_And_FetchesFirstPage()
    {
        // 1. Arrange: In-memory DB, library directory and services
        var libDir = Path.Combine(_tempDir, "MyComics");
        Directory.CreateDirectory(libDir);

        var configId = await _db.Connection.QuerySingleAsync<long>("""
            INSERT INTO "LibraryConfigs" ("LibraryPattern") VALUES ('SeriesBased') RETURNING "Id";
            """);

        var libraryId = Guid.NewGuid().ToString();
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'MyComics', @Path, 'Ready', @ConfigId, @Now);
            """, new { Id = libraryId, Path = libDir, ConfigId = configId, Now = DateTimeOffset.UtcNow });

        var compositeProcessor = new CompositeBookProcessor(new IBookProcessor[]
        {
            new ZipBookProcessor(),
            new RarBookProcessor(),
            new EpubBookProcessor()
        });
        var queue = new ScannerQueue();
        var v2Service = new DiarSpeicherService(_db.Factory, compositeProcessor, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var scannerService = new LibraryScannerService(
            _db.Factory,
            new DirectoryScanner(),
            compositeProcessor,
            new ThumbnailService(compositeProcessor, NullLogger<ThumbnailService>.Instance),
            new ArchiveConversionService(NullLogger<ArchiveConversionService>.Instance),
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
                new DiarSpeicherUploadFileInput { FileName = "Batman_01.cbz", Content = cbzStream }
            });

        Assert.Equal(UploadOutcome.Success, uploadResult.Outcome);
        Assert.NotNull(uploadResult.Response);
        Assert.Equal(1, uploadResult.Response!.UploadedCount);
        Assert.True(uploadResult.Response.ScanJobTriggered);

        var expectedFile = Path.Combine(libDir, "Batman", "Batman_01.cbz");
        Assert.True(File.Exists(expectedFile));

        // 3. Scan the library (as the background worker would do)
        var scanReport = await scannerService.ScanLibraryAsync(libraryId);
        Assert.True(scanReport.Success);
        Assert.Equal(1UL, scanReport.CreatedSeries);
        Assert.Equal(1UL, scanReport.CreatedMedia);

        // 4. Verify Media was persisted with correct pages and metadata
        var media = await _db.Connection.QueryFirstOrDefaultAsync<Media>(
            """SELECT * FROM "Media" WHERE "Name" = 'Batman Year One'""");
        Assert.NotNull(media);
        Assert.Equal(3, media.Pages);
        Assert.Equal("cbz", media.Extension);

        // 5. Fetch the first page via DiarSpeicher v2 Service!
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
