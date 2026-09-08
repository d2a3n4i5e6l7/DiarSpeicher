using System.IO.Compression;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class KoboSyncTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _tempEpubPath;
    private string _kidEpubId = null!;
    private string _adultEpubId = null!;

    public KoboSyncTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();

        _tempEpubPath = Path.Combine(Path.GetTempPath(), $"kobo_test_{Guid.NewGuid():N}.epub");
        CreateDummyEpub(_tempEpubPath);

        SeedTestData(context);
    }

    private static void CreateDummyEpub(string filePath)
    {
        using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("mimetype");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("application/epub+zip");
    }

    private void SeedTestData(DiarSpeicherDbContext context)
    {
        var cfg = new LibraryConfig();
        context.LibraryConfigs.Add(cfg);
        context.SaveChanges();

        var lib = new Library
        {
            Name = "Books",
            Path = "/media/books",
            ConfigId = cfg.Id
        };
        context.Libraries.Add(lib);
        context.SaveChanges();

        var series = new Series
        {
            Name = "Fantasy",
            Path = "/media/books/fantasy",
            LibraryId = lib.Id
        };
        context.Series.Add(series);
        context.SaveChanges();

        var kidBook = new Media
        {
            Name = "The Hobbit",
            Path = _tempEpubPath,
            Extension = "epub",
            Size = 1024,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "The Hobbit: An Unexpected Journey",
                Writers = "J.R.R. Tolkien",
                Genres = "Fantasy, Adventure",
                AgeRating = 8,
                Number = 1m
            }
        };

        var adultBook = new Media
        {
            Name = "A Game of Thrones",
            Path = _tempEpubPath,
            Extension = "epub",
            Size = 2048,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "A Song of Ice and Fire",
                Writers = "George R.R. Martin",
                Genres = "Dark Fantasy",
                AgeRating = 18,
                Number = 1m
            }
        };

        var comicBook = new Media
        {
            Name = "Spider-Man",
            Path = "/media/books/fantasy/spiderman.cbz",
            Extension = "cbz",
            Size = 5000,
            SeriesId = series.Id
        };

        context.Media.AddRange(kidBook, adultBook, comicBook);
        context.SaveChanges();

        _kidEpubId = kidBook.Id;
        _adultEpubId = adultBook.Id;
    }

    [Fact]
    public async Task GetInitializationAsync_ReturnsFormattedResourcesDictionary()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoboService(context, NullLogger<KoboService>.Instance);

        var resources = await service.GetInitializationAsync("http://localhost:5000", "my-api-key");

        Assert.Equal("http://localhost:5000", resources["image_host"]);
        Assert.Contains("{ImageId}", resources["image_url_template"]?.ToString());
        Assert.Contains("my-api-key", resources["image_url_template"]?.ToString());
    }

    [Fact]
    public async Task SyncLibraryAsync_SyncsOnlyEpubs_AndRespectsParentalAgeRating()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoboService(context, NullLogger<KoboService>.Instance);

        var childUser = new AuthUser
        {
            Id = "frodo",
            Username = "frodo",
            IsServerOwner = false,
            AgeRestriction = 10
        };

        var syncResult = await service.SyncLibraryAsync(childUser, "http://localhost:5000", "key123", null, 100);

        Assert.Single(syncResult.Items);
        Assert.Equal(_kidEpubId, syncResult.Items[0].BookEntitlement.Id);
        Assert.Equal("The Hobbit: An Unexpected Journey", syncResult.Items[0].BookMetadata.Title);
        Assert.Equal("J.R.R. Tolkien", syncResult.Items[0].BookMetadata.Contributors[0]);
        Assert.False(syncResult.ShouldContinue);
        Assert.NotNull(syncResult.SyncToken);
    }

    [Fact]
    public async Task SyncLibraryAsync_OwnerSeesAllEpubs_ExcludesCbz()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoboService(context, NullLogger<KoboService>.Instance);

        var owner = new AuthUser
        {
            Id = "admin",
            Username = "admin",
            IsServerOwner = true
        };

        var syncResult = await service.SyncLibraryAsync(owner, "http://localhost:5000", "key123", null, 100);

        // 2 epubs (kidBook and adultBook), 0 cbz
        Assert.Equal(2, syncResult.Items.Count);
        Assert.Contains(syncResult.Items, i => i.BookEntitlement.Id == _kidEpubId);
        Assert.Contains(syncResult.Items, i => i.BookEntitlement.Id == _adultEpubId);
    }

    [Fact]
    public async Task GetBookMetadataAsync_And_GetBookFileAsync_WorkCorrectly()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KoboService(context, NullLogger<KoboService>.Instance);

        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };

        var metadata = await service.GetBookMetadataAsync(owner, "http://localhost:5000", "key", _kidEpubId);
        Assert.NotNull(metadata);
        Assert.Equal("The Hobbit: An Unexpected Journey", metadata.Title);
        Assert.Equal("Fantasy", metadata.Series?.Name);

        var file = await service.GetBookFileAsync(owner, _kidEpubId);
        Assert.NotNull(file);
        Assert.Equal(_tempEpubPath, file.Value.Path);
        Assert.Equal("application/epub+zip", file.Value.ContentType);
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
