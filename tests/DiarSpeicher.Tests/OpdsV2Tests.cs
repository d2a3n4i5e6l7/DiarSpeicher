using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class OpdsV2Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private string _kidBookId = null!;
    private string _adultBookId = null!;

    public OpdsV2Tests()
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

        var lib = new Library { Name = "Manga & Comics", Path = "/data/comics", ConfigId = cfg.Id };
        context.Libraries.Add(lib);
        context.SaveChanges();

        var series = new Series { Name = "One Piece", Path = "/data/comics/op", LibraryId = lib.Id };
        context.Series.Add(series);
        context.SaveChanges();

        var kidBook = new Media
        {
            Name = "One Piece Vol 01",
            Path = "/data/comics/op/01.cbz",
            Extension = "cbz",
            Pages = 200,
            Size = 50000000,
            SeriesId = series.Id,
            Metadata = new MediaMetadata { Title = "Romance Dawn", AgeRating = 10 }
        };

        var adultBook = new Media
        {
            Name = "Berserk Vol 01",
            Path = "/data/comics/op/b01.cbz",
            Extension = "cbz",
            Pages = 220,
            Size = 60000000,
            SeriesId = series.Id,
            Metadata = new MediaMetadata { Title = "The Black Swordsman", AgeRating = 18 }
        };

        context.Media.AddRange(kidBook, adultBook);

        var readerUser = new User
        {
            Id = "reader_v2",
            Username = "luffy"
        };
        context.Users.Add(readerUser);

        context.SaveChanges();

        _kidBookId = kidBook.Id;
        _adultBookId = adultBook.Id;
    }

    [Fact]
    public void GetAuthenticationDoc_ReturnsValidAuthDocument()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance);

        var authDoc = service.GetAuthenticationDoc(null);

        Assert.NotNull(authDoc);
        Assert.Equal("http://opds-spec.org/auth/basic", authDoc.Type);
        Assert.NotEmpty(authDoc.Links);
    }

    [Fact]
    public async Task GetCatalogFeedAsync_ReturnsJsonLdFeedWithNavigationAndGroups()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance);

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var feed = await service.GetCatalogFeedAsync(user, null);

        Assert.NotNull(feed);
        Assert.Equal("https://readium.org/webpub-manifest/context.jsonld", feed.Context);
        Assert.NotEmpty(feed.Navigation);
        Assert.Contains(feed.Navigation, link => link.Href.Contains("libraries"));
        Assert.Contains(feed.Navigation, link => link.Href.Contains("series"));
        Assert.Contains(feed.Navigation, link => link.Href.Contains("books"));
    }

    [Fact]
    public async Task GetPublicationAsync_ReturnsValidManifestWithReadingOrder()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance);

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var pub = await service.GetPublicationAsync(user, _kidBookId, null);

        Assert.NotNull(pub);
        Assert.Equal("Romance Dawn", pub.Metadata.Title);
        Assert.Equal(200, pub.Metadata.NumberOfPages);
        Assert.NotEmpty(pub.Images);
        Assert.NotEmpty(pub.Links);

        // Verifica que contenga las 200 páginas en el readingOrder
        Assert.Equal(200, pub.ReadingOrder.Count);
        Assert.Equal("/opds/v2.0/books/" + _kidBookId + "/pages/0?zero_based=true", pub.ReadingOrder[0].Href);
    }

    [Fact]
    public async Task Progression_CanUpdateAndRetrieveProgress()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance);

        var user = new AuthUser { Id = "reader_v2", Username = "luffy", IsServerOwner = false };

        // 1. Actualizar progreso a página 45
        var updateSuccess = await service.UpdateProgressionAsync(user, _kidBookId, new OpdsV2Progression
        {
            Page = 45,
            Percentage = 0.225m
        });

        Assert.True(updateSuccess);

        // 2. Recuperar progreso
        var prog = await service.GetProgressionAsync(user, _kidBookId);
        Assert.NotNull(prog);
        Assert.Equal(45, prog.Page);
        Assert.Equal(0.225m, prog.Percentage);

        // 3. Verificar estado en base de datos
        var session = await context.ReadingSessions
            .FirstOrDefaultAsync(s => s.MediaId == _kidBookId && s.UserId == "reader_v2");
        Assert.NotNull(session);
        Assert.Equal(45, session.EndPage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
    }

    [Fact]
    public async Task GetBooksFeedAsync_EnforcesParentalAgeRestrictionInOpdsV2()
    {
        using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance);

        var childUser = new AuthUser
        {
            Id = "child_user",
            Username = "chopper",
            IsServerOwner = false,
            AgeRestriction = 12
        };

        var feed = await service.GetBooksFeedAsync(childUser, 0, null);

        Assert.Single(feed.Publications);
        Assert.Equal("Romance Dawn", feed.Publications[0].Metadata.Title);
        Assert.DoesNotContain(feed.Publications, p => p.Metadata.Identifier?.Contains(_adultBookId) == true);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
