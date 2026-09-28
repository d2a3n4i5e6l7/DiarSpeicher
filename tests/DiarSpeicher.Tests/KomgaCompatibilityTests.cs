using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Komga;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class KomgaCompatibilityTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private string _libraryId = null!;
    private string _seriesId = null!;
    private string _kidBookId = null!;
    private string _adultBookId = null!;

    public KomgaCompatibilityTests()
    {
        _db = new TestSqliteDatabase();
        SeedTestData();
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        conn.Execute("""INSERT INTO "LibraryConfigs" ("Id") VALUES (1);""");

        _libraryId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Manga', '/data/manga', 'Ready', 1, @Now);
            """, new { Id = _libraryId, Now = DateTimeOffset.UtcNow });

        _seriesId = Guid.NewGuid().ToString();
        conn.Execute("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Naruto', '/data/manga/naruto', @LibraryId, 'Ready', @Now);
            """, new { Id = _seriesId, LibraryId = _libraryId, Now = DateTimeOffset.UtcNow });

        _kidBookId = Guid.NewGuid().ToString();
        _adultBookId = Guid.NewGuid().ToString();

        conn.Execute("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "Size", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'cbz', @Pages, @Size, @SeriesId, 'Ready', @Now);
            """, new[]
        {
            new { Id = _kidBookId, Name = "Naruto Vol 01", Path = "/data/manga/naruto/01.cbz", Pages = 180, Size = 40000000L, SeriesId = _seriesId, Now = DateTimeOffset.UtcNow },
            new { Id = _adultBookId, Name = "Berserk Vol 01", Path = "/data/manga/naruto/b01.cbz", Pages = 220, Size = 55000000L, SeriesId = _seriesId, Now = DateTimeOffset.UtcNow }
        });

        conn.Execute("""
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "AgeRating", "Writers")
            VALUES (@MediaId, @Title, @AgeRating, @Writers);
            """, new[]
        {
            new { MediaId = _kidBookId, Title = "Enter: Naruto Uzumaki!", AgeRating = (int?)12, Writers = "Masashi Kishimoto" },
            new { MediaId = _adultBookId, Title = "The Black Swordsman", AgeRating = (int?)18, Writers = "Kentaro Miura" }
        });

        conn.Execute("""
            INSERT INTO "Users" ("Id", "Username", "CreatedAt")
            VALUES ('reader_komga', 'narutofan', @Now);
            """, new { Now = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task GetLibrariesAsync_ReturnsKomgaFormattedLibraries()
    {
        var service = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var libs = await service.GetLibrariesAsync(user);

        Assert.Single(libs);
        Assert.Equal("Manga", libs[0].Name);
        Assert.Equal("/data/manga", libs[0].Root);
        Assert.False(libs[0].Unavailable);
    }

    [Fact]
    public async Task GetSeriesAsync_ReturnsSpringPageableFormat()
    {
        var service = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var page = await service.GetSeriesAsync(user, _libraryId, null, 0, 20);

        Assert.NotNull(page);
        Assert.Single(page.Content);
        Assert.Equal(1, page.TotalElements);
        Assert.Equal(1, page.TotalPages);
        Assert.True(page.First);
        Assert.True(page.Last);
        Assert.Equal(0, page.Number);
        Assert.Equal(20, page.Size);

        var series = page.Content[0];
        Assert.Equal("Naruto", series.Name);
        Assert.Equal(2, series.BooksCount);
    }

    [Fact]
    public async Task GetBookPagesAsync_ReturnsCorrectPageList()
    {
        var service = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var pages = await service.GetBookPagesAsync(user, _kidBookId);

        Assert.Equal(180, pages.Count);
        Assert.Equal(1, pages[0].Number);
        Assert.Equal("page_0001.jpg", pages[0].FileName);
        Assert.Equal("image/jpeg", pages[0].MediaType);
        Assert.Equal(180, pages[179].Number);
    }

    [Fact]
    public async Task ReadProgress_CanUpdateAndDeleteProgress()
    {
        var service = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "reader_komga", Username = "narutofan", IsServerOwner = false };

        // 1. Actualizar progreso a pagina 50
        var updateSuccess = await service.UpdateReadProgressAsync(user, _kidBookId, 50, false);
        Assert.True(updateSuccess);

        var book = await service.GetBookByIdAsync(user, _kidBookId);
        Assert.NotNull(book);
        Assert.NotNull(book.ReadProgress);
        Assert.Equal(50, book.ReadProgress.Page);
        Assert.False(book.ReadProgress.Completed);

        // 2. Borrar progreso
        var deleteSuccess = await service.DeleteReadProgressAsync(user, _kidBookId);
        Assert.True(deleteSuccess);

        var sessionInDb = await _db.Connection.QueryFirstOrDefaultAsync<ReadingSession>(
            """SELECT * FROM "ReadingSessions" WHERE "MediaId" = @MediaId AND "UserId" = @UserId""",
            new { MediaId = _kidBookId, UserId = "reader_komga" });
        Assert.Null(sessionInDb);
    }

    [Fact]
    public async Task GetBooksInSeriesAsync_EnforcesParentalAgeRestriction()
    {
        var service = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, TestReadingProgress.For(_db.Factory));

        var childUser = new AuthUser
        {
            Id = "child_user",
            Username = "konohamaru",
            IsServerOwner = false,
            AgeRestriction = 14
        };

        var page = await service.GetBooksInSeriesAsync(childUser, _seriesId, 0, 20);

        Assert.Single(page.Content);
        Assert.Equal("Naruto Vol 01", page.Content[0].Name);
        Assert.Equal("Enter: Naruto Uzumaki!", page.Content[0].Metadata.Title);
        Assert.DoesNotContain(page.Content, b => b.Id == _adultBookId);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
