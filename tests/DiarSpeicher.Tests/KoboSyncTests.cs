using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class KoboSyncTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _tempEpubPath;
    private string _kidEpubId = null!;
    private string _adultEpubId = null!;

    public KoboSyncTests()
    {
        _db = new TestSqliteDatabase();

        _tempEpubPath = Path.Combine(Path.GetTempPath(), $"kobo_test_{Guid.NewGuid():N}.epub");
        CreateDummyEpub(_tempEpubPath);

        SeedTestData();
    }

    private static void CreateDummyEpub(string filePath)
    {
        using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("mimetype");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("application/epub+zip");
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        conn.Execute("""INSERT INTO "LibraryConfigs" ("Id") VALUES (1);""");

        var libId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Books', '/media/books', 'Ready', 1, @Now);
            """, new { Id = libId, Now = DateTimeOffset.UtcNow });

        var seriesId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Fantasy', '/media/books/fantasy', @LibraryId, 'Ready', @Now);
            """, new { Id = seriesId, LibraryId = libId, Now = DateTimeOffset.UtcNow });

        var kidBookId = Guid.NewGuid().ToString();
        var adultBookId = Guid.NewGuid().ToString();
        var comicBookId = Guid.NewGuid().ToString();
        _kidEpubId = kidBookId;
        _adultEpubId = adultBookId;

        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "Size", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, @Extension, 0, @Size, @SeriesId, 'Ready', @Now);
            """, new[]
        {
            new { Id = kidBookId, Name = "The Hobbit", Path = _tempEpubPath, Extension = "epub", Size = 1024L, SeriesId = seriesId, Now = DateTimeOffset.UtcNow },
            new { Id = adultBookId, Name = "A Game of Thrones", Path = _tempEpubPath, Extension = "epub", Size = 2048L, SeriesId = seriesId, Now = DateTimeOffset.UtcNow },
            new { Id = comicBookId, Name = "Spider-Man", Path = "/media/books/fantasy/spiderman.cbz", Extension = "cbz", Size = 5000L, SeriesId = seriesId, Now = DateTimeOffset.UtcNow }
        });

        conn.Execute("""
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "Writers", "Genres", "AgeRating", "Number")
            VALUES (@MediaId, @Title, @Writers, @Genres, @AgeRating, @Number);
            """, new[]
        {
            new { MediaId = kidBookId, Title = "The Hobbit: An Unexpected Journey", Writers = "J.R.R. Tolkien", Genres = "Fantasy, Adventure", AgeRating = (int?)8, Number = (decimal?)1m },
            new { MediaId = adultBookId, Title = "A Song of Ice and Fire", Writers = "George R.R. Martin", Genres = "Dark Fantasy", AgeRating = (int?)18, Number = (decimal?)1m }
        });
    }

    [Fact]
    public async Task GetInitializationAsync_ReturnsFormattedResourcesDictionary()
    {
        var service = new KoboService(_db.Factory, NullLogger<KoboService>.Instance);

        var resources = await service.GetInitializationAsync("http://localhost:5000", "my-api-key");

        Assert.Equal("http://localhost:5000", resources["image_host"]);
        Assert.Contains("{ImageId}", resources["image_url_template"]?.ToString());
        Assert.Contains("my-api-key", resources["image_url_template"]?.ToString());
    }

    [Fact]
    public async Task SyncLibraryAsync_SyncsOnlyEpubs_AndRespectsParentalAgeRating()
    {
        var service = new KoboService(_db.Factory, NullLogger<KoboService>.Instance);

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
        var service = new KoboService(_db.Factory, NullLogger<KoboService>.Instance);

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
        var service = new KoboService(_db.Factory, NullLogger<KoboService>.Instance);

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
        _db.Dispose();
        if (File.Exists(_tempEpubPath))
        {
            File.Delete(_tempEpubPath);
        }
    }
}
