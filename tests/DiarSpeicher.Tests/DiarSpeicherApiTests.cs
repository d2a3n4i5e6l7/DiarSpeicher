using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class DiarSpeicherApiTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _tempEpubPath;
    private string _libraryId = null!;
    private string _seriesId = null!;
    private string _epubMediaId = null!;

    public DiarSpeicherApiTests()
    {
        _db = new TestSqliteDatabase();

        _tempEpubPath = Path.Combine(Path.GetTempPath(), $"diarspeicher_test_{Guid.NewGuid():N}.epub");
        CreateSampleEpubWithToc(_tempEpubPath);

        SeedTestData();
    }

    private static void CreateSampleEpubWithToc(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var ncx = zip.CreateEntry("toc.ncx");
        using (var writer = new StreamWriter(ncx.Open()))
        {
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                    <navMap>
                        <navPoint id="c1">
                            <navLabel><text>Chapter 1: The Beginning</text></navLabel>
                            <content src="chapter1.xhtml"/>
                        </navPoint>
                        <navPoint id="c2">
                            <navLabel><text>Chapter 2: The Journey</text></navLabel>
                            <content src="chapter2.xhtml"/>
                        </navPoint>
                    </navMap>
                </ncx>
                """);
        }

        var ch1 = zip.CreateEntry("chapter1.xhtml");
        using (var writer = new StreamWriter(ch1.Open()))
        {
            writer.Write("<html><body><h1>Chapter 1</h1><p>Hello DiarSpeicher</p></body></html>");
        }

        var style = zip.CreateEntry("style.css");
        using (var writer = new StreamWriter(style.Open()))
        {
            writer.Write("body { color: #333; }");
        }
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        conn.Execute("""INSERT INTO "LibraryConfigs" ("Id") VALUES (1);""");

        conn.Execute("""
            INSERT INTO "Users" ("Id", "Username", "IsServerOwner", "CreatedAt")
            VALUES ('user_admin', 'admin', 1, @Now);
            """, new { Now = DateTimeOffset.UtcNow });

        _libraryId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Books', '/books', 'Ready', 1, @Now);
            """, new { Id = _libraryId, Now = DateTimeOffset.UtcNow });

        _seriesId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Epic Saga', '/books/epic', @LibraryId, 'Ready', @Now);
            """, new { Id = _seriesId, LibraryId = _libraryId, Now = DateTimeOffset.UtcNow });

        _epubMediaId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, 'Epic Book 1', @Path, 'epub', 250, @SeriesId, 'Ready', @Now);
            """, new { Id = _epubMediaId, Path = _tempEpubPath, SeriesId = _seriesId, Now = DateTimeOffset.UtcNow });

        conn.Execute("""
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "Writers", "Genres", "AgeRating")
            VALUES (@MediaId, 'The Awakening', 'Author Name', 'Sci-Fi', 0);
            """, new { MediaId = _epubMediaId });

        conn.Execute("""
            INSERT INTO "ReadingSessions" ("UserId", "MediaId", "StartPage", "EndPage", "StartPercentage", "EndPercentage", "Status", "SessionDate", "CreatedAt", "UpdatedAt")
            VALUES ('user_admin', @MediaId, 1, 50, '0', '0.2', @Status, @Now, @Now, @Now);
            """, new { MediaId = _epubMediaId, Status = nameof(ReadingStatus.Reading), Now = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task GetMediaAsync_And_GetKeepReadingAsync_ReturnValidData()
    {
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(_db.Factory, composite, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "user_admin", Username = "admin", IsServerOwner = true };

        var page = await service.GetMediaAsync(user, 0, 20);
        Assert.Single(page.Data);
        Assert.Equal("Epic Book 1", page.Data[0].Name);
        Assert.Equal(50, page.Data[0].CurrentPage);
        Assert.False(page.Data[0].IsCompleted);

        var keepReading = await service.GetKeepReadingAsync(user);
        Assert.Single(keepReading);
        Assert.Equal(_epubMediaId, keepReading[0].Id);
    }

    [Fact]
    public async Task GetSeriesAsync_And_GetLibrariesAsync_ReturnConfiguredHierarchy()
    {
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(_db.Factory, composite, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "user_admin", Username = "admin", IsServerOwner = true };

        var libraries = await service.GetLibrariesAsync(user);
        Assert.Single(libraries);
        Assert.Equal("Books", libraries[0].Name);

        var seriesPage = await service.GetSeriesAsync(user, _libraryId, 0, 20);
        Assert.Single(seriesPage.Data);
        Assert.Equal("Epic Saga", seriesPage.Data[0].Name);

        var seriesMedia = await service.GetSeriesMediaAsync(user, _seriesId, 0, 20);
        Assert.Single(seriesMedia.Data);
        Assert.Equal(_epubMediaId, seriesMedia.Data[0].Id);
    }

    [Fact]
    public async Task TriggerLibraryScanAsync_EnqueuesScanTaskInQueue()
    {
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(_db.Factory, composite, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "user_admin", Username = "admin", IsServerOwner = true };

        var enqueued = await service.TriggerLibraryScanAsync(user, _libraryId);
        Assert.True(enqueued);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var enumerator = queue.ReadRequestsAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(_libraryId, enumerator.Current.LibraryId);
    }

    [Fact]
    public async Task GetEpubTocAsync_And_GetEpubResourceAsync_ExtractInternalAssets()
    {
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(_db.Factory, composite, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "user_admin", Username = "admin", IsServerOwner = true };

        var toc = await service.GetEpubTocAsync(user, _epubMediaId);
        Assert.NotNull(toc);
        Assert.Equal(2, toc.Items.Count);
        Assert.Equal("Chapter 1: The Beginning", toc.Items[0].Title);
        Assert.Equal("chapter1.xhtml", toc.Items[0].Href);
        Assert.Equal("Chapter 2: The Journey", toc.Items[1].Title);

        var htmlResource = await service.GetEpubResourceAsync(user, _epubMediaId, "chapter1.xhtml");
        Assert.NotNull(htmlResource);
        Assert.Equal("application/xhtml+xml", htmlResource.Value.ContentType);
        Assert.Contains("Hello DiarSpeicher", System.Text.Encoding.UTF8.GetString(htmlResource.Value.Data));

        var cssResource = await service.GetEpubResourceAsync(user, _epubMediaId, "style.css");
        Assert.NotNull(cssResource);
        Assert.Equal("text/css", cssResource.Value.ContentType);
    }

    [Fact]
    public async Task GetSystemStatusAsync_ReturnsClaimedAndHealthy()
    {
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new DiarSpeicherService(_db.Factory, composite, queue, NullLogger<DiarSpeicherService>.Instance, TestStorageOptions.Default(), TestReadingProgress.For(_db.Factory));

        var status = await service.GetSystemStatusAsync();
        Assert.Equal("OK", status.Status);
        Assert.Equal("0.1.0", status.Semver);
        Assert.True(status.IsClaimed);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempEpubPath))
        {
            File.Delete(_tempEpubPath);
        }
    }
}
