using System.IO.Compression;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.StumpV2;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class StumpV2ApiTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _tempEpubPath;
    private string _libraryId = null!;
    private string _seriesId = null!;
    private string _epubMediaId = null!;

    public StumpV2ApiTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();

        _tempEpubPath = Path.Combine(Path.GetTempPath(), $"stump_test_{Guid.NewGuid():N}.epub");
        CreateSampleEpubWithToc(_tempEpubPath);

        SeedTestData(context);
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

    private void SeedTestData(DiarSpeicherDbContext context)
    {
        var cfg = new LibraryConfig();
        context.LibraryConfigs.Add(cfg);

        var user = new User
        {
            Id = "user_admin",
            Username = "admin",
            IsServerOwner = true
        };
        context.Users.Add(user);
        context.SaveChanges();

        var lib = new Library
        {
            Name = "Books",
            Path = "/books",
            ConfigId = cfg.Id
        };
        context.Libraries.Add(lib);
        context.SaveChanges();
        _libraryId = lib.Id;

        var series = new Series
        {
            Name = "Epic Saga",
            Path = "/books/epic",
            LibraryId = lib.Id
        };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        var media = new Media
        {
            Name = "Epic Book 1",
            Path = _tempEpubPath,
            Extension = "epub",
            Pages = 250,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "The Awakening",
                Writers = "Author Name",
                Genres = "Sci-Fi",
                AgeRating = 0
            }
        };
        context.Media.Add(media);
        context.SaveChanges();
        _epubMediaId = media.Id;

        var session = new ReadingSession
        {
            UserId = user.Id,
            MediaId = media.Id,
            StartPage = 1,
            EndPage = 50,
            StartPercentage = 0m,
            EndPercentage = 0.2m,
            Status = ReadingStatus.Reading
        };
        context.ReadingSessions.Add(session);
        context.SaveChanges();
    }

    [Fact]
    public async Task GetMediaAsync_And_GetKeepReadingAsync_ReturnValidData()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(context, composite, queue, NullLogger<StumpV2Service>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(context, composite, queue, NullLogger<StumpV2Service>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(context, composite, queue, NullLogger<StumpV2Service>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(context, composite, queue, NullLogger<StumpV2Service>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var composite = new CompositeBookProcessor(new List<IBookProcessor> { new EpubBookProcessor() });
        var queue = new ScannerQueue();
        var service = new StumpV2Service(context, composite, queue, NullLogger<StumpV2Service>.Instance);

        var status = await service.GetSystemStatusAsync();
        Assert.Equal("OK", status.Status);
        Assert.Equal("0.1.0", status.Semver);
        Assert.True(status.IsClaimed);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (File.Exists(_tempEpubPath))
        {
            File.Delete(_tempEpubPath);
        }
    }
}
