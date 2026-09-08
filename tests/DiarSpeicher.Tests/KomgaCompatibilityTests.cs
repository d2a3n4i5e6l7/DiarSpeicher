using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Komga;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class KomgaCompatibilityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private string _libraryId = null!;
    private string _seriesId = null!;
    private string _kidBookId = null!;
    private string _adultBookId = null!;

    public KomgaCompatibilityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();

        SeedTestData(context);
    }

    private void SeedTestData(DiarSpeicherDbContext context)
    {
        var cfg = new LibraryConfig();
        context.LibraryConfigs.Add(cfg);
        context.SaveChanges();

        var lib = new Library { Name = "Manga", Path = "/data/manga", ConfigId = cfg.Id };
        context.Libraries.Add(lib);
        context.SaveChanges();
        _libraryId = lib.Id;

        var series = new Series { Name = "Naruto", Path = "/data/manga/naruto", LibraryId = lib.Id };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        var kidBook = new Media
        {
            Name = "Naruto Vol 01",
            Path = "/data/manga/naruto/01.cbz",
            Extension = "cbz",
            Pages = 180,
            Size = 40000000,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "Enter: Naruto Uzumaki!",
                AgeRating = 12,
                Writers = "Masashi Kishimoto"
            }
        };

        var adultBook = new Media
        {
            Name = "Berserk Vol 01",
            Path = "/data/manga/naruto/b01.cbz",
            Extension = "cbz",
            Pages = 220,
            Size = 55000000,
            SeriesId = series.Id,
            Metadata = new MediaMetadata
            {
                Title = "The Black Swordsman",
                AgeRating = 18,
                Writers = "Kentaro Miura"
            }
        };

        context.Media.AddRange(kidBook, adultBook);

        var readerUser = new User
        {
            Id = "reader_komga",
            Username = "narutofan"
        };
        context.Users.Add(readerUser);

        context.SaveChanges();

        _kidBookId = kidBook.Id;
        _adultBookId = adultBook.Id;
    }

    [Fact]
    public async Task GetLibrariesAsync_ReturnsKomgaFormattedLibraries()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KomgaService(context, NullLogger<KomgaService>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KomgaService(context, NullLogger<KomgaService>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KomgaService(context, NullLogger<KomgaService>.Instance);

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
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KomgaService(context, NullLogger<KomgaService>.Instance);

        var user = new AuthUser { Id = "reader_komga", Username = "narutofan", IsServerOwner = false };

        // 1. Actualizar progreso a página 50
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

        var sessionInDb = await context.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == _kidBookId && s.UserId == "reader_komga");
        Assert.Null(sessionInDb);
    }

    [Fact]
    public async Task GetBooksInSeriesAsync_EnforcesParentalAgeRestriction()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new KomgaService(context, NullLogger<KomgaService>.Instance);

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
        _connection.Dispose();
    }
}
