using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class ForUserFilterTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    private string _adultLibraryId = null!;

    public ForUserFilterTests()
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
        var cfg1 = new LibraryConfig();
        var cfg2 = new LibraryConfig();
        context.LibraryConfigs.AddRange(cfg1, cfg2);
        context.SaveChanges();

        var kidsLib = new Library { Name = "Kids", Path = "/data/kids", ConfigId = cfg1.Id };
        var adultLib = new Library { Name = "Adults Only", Path = "/data/adults", ConfigId = cfg2.Id };
        context.Libraries.AddRange(kidsLib, adultLib);
        context.SaveChanges();

        // 1. Serie para niños con rating explícito en la serie = 8
        var kidsSeriesWithRating = new Series
        {
            Name = "Donald Duck",
            Path = "/data/kids/dd",
            LibraryId = kidsLib.Id,
            Metadata = new SeriesMetadata { AgeRating = 8 }
        };

        // 2. Serie sin metadata
        var seriesWithoutMeta = new Series
        {
            Name = "Unrated Series",
            Path = "/data/kids/unrated",
            LibraryId = kidsLib.Id
        };

        // 3. Serie para adultos con rating = 18
        var adultSeries = new Series
        {
            Name = "Berserk",
            Path = "/data/adults/berserk",
            LibraryId = adultLib.Id,
            Metadata = new SeriesMetadata { AgeRating = 18 }
        };

        context.Series.AddRange(kidsSeriesWithRating, seriesWithoutMeta, adultSeries);
        context.SaveChanges();

        // Libro con rating propio explícito = 6
        var bookExplicit6 = new Media
        {
            Name = "Donald 1.cbz",
            Path = "/data/kids/dd/1.cbz",
            Extension = "cbz",
            SeriesId = kidsSeriesWithRating.Id,
            Metadata = new MediaMetadata { Title = "Duck 1", AgeRating = 6 }
        };

        // Libro sin rating en metadata, pero defiere a su serie que tiene rating = 8
        var bookDeferred8 = new Media
        {
            Name = "Donald 2.cbz",
            Path = "/data/kids/dd/2.cbz",
            Extension = "cbz",
            SeriesId = kidsSeriesWithRating.Id,
            Metadata = new MediaMetadata { Title = "Duck 2", AgeRating = null }
        };

        // Libro sin rating y serie sin metadata
        var bookUnrated = new Media
        {
            Name = "Unknown.cbz",
            Path = "/data/kids/unrated/unrated.cbz",
            Extension = "cbz",
            SeriesId = seriesWithoutMeta.Id
        };

        // Libro de adultos con rating = 18
        var bookMature = new Media
        {
            Name = "Berserk 1.cbz",
            Path = "/data/adults/berserk/1.cbz",
            Extension = "cbz",
            SeriesId = adultSeries.Id,
            Metadata = new MediaMetadata { Title = "Berserk 1", AgeRating = 18 }
        };

        context.Media.AddRange(bookExplicit6, bookDeferred8, bookUnrated, bookMature);
        context.SaveChanges();

        _adultLibraryId = adultLib.Id;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task ServerOwner_Sees_All_Media()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var owner = new AuthUser { Username = "admin", IsServerOwner = true };

        var books = await context.Media
            .Include(m => m.Series)
            .Include(m => m.Metadata)
            .ForUser(owner)
            .ToListAsync();

        Assert.Equal(4, books.Count);
    }

    [Fact]
    public async Task User_With_Excluded_Library_Does_Not_See_Media_In_It()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var user = new AuthUser
        {
            Username = "user1",
            ExcludedLibraryIds = new HashSet<string> { _adultLibraryId }
        };

        var books = await context.Media
            .Include(m => m.Series)
            .ForUser(user)
            .ToListAsync();

        Assert.Equal(3, books.Count);
        Assert.DoesNotContain(books, b => b.Series != null && b.Series.LibraryId == _adultLibraryId);
    }

    [Fact]
    public async Task RestrictOnUnset_Allows_Explicit_And_Deferred_Ratings_Under_Max()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var childUser = new AuthUser
        {
            Username = "child",
            AgeRestriction = 10,
            RestrictOnUnset = true
        };

        var books = await context.Media
            .Include(m => m.Series)
                .ThenInclude(s => s!.Metadata)
            .Include(m => m.Metadata)
            .ForUser(childUser)
            .ToListAsync();

        // 1. Donald 1 (explicit 6 <= 10) -> ALLOW
        // 2. Donald 2 (deferred to Series 8 <= 10) -> ALLOW
        // 3. Unknown (unrated book & unrated series) -> DENIED
        // 4. Berserk (18 > 10) -> DENIED
        Assert.Equal(2, books.Count);
        Assert.Contains(books, b => b.Name == "Donald 1.cbz");
        Assert.Contains(books, b => b.Name == "Donald 2.cbz");
        Assert.DoesNotContain(books, b => b.Name == "Unknown.cbz");
        Assert.DoesNotContain(books, b => b.Name == "Berserk 1.cbz");
    }

    [Fact]
    public async Task PermissiveMode_Allows_Unrated_Media_And_Series()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var teenUser = new AuthUser
        {
            Username = "teen",
            AgeRestriction = 14,
            RestrictOnUnset = false
        };

        var books = await context.Media
            .Include(m => m.Series)
                .ThenInclude(s => s!.Metadata)
            .Include(m => m.Metadata)
            .ForUser(teenUser)
            .ToListAsync();

        // Donald 1 (6), Donald 2 (8), Unknown (unrated series allowed in permissive mode)
        // Berserk 1 (18 > 14) -> DENIED
        Assert.Equal(3, books.Count);
        Assert.DoesNotContain(books, b => b.Name == "Berserk 1.cbz");
    }
}
