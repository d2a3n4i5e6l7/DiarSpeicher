using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

/// <summary>
/// Acceptance criteria of block B2.1 for OPDS 2.0: without the library and series feeds a
/// client can list libraries but cannot open any of them, so navigation is broken.
/// </summary>
public sealed class OpdsV2NavigationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly AuthUser _owner = new() { Id = "admin", Username = "admin", IsServerOwner = true };

    private string _libraryId = null!;
    private string _seriesId = null!;

    public OpdsV2NavigationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();
        Seed(context);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Seed(DiarSpeicherDbContext context)
    {
        var library = new Library
        {
            Name = "Comics",
            Path = "/libros",
            Status = FileStatus.Ready,
            Config = new LibraryConfig()
        };

        context.Libraries.Add(library);
        context.SaveChanges();
        _libraryId = library.Id;

        var series = new Series
        {
            Name = "Batman",
            Path = "/libros/Batman",
            LibraryId = library.Id,
            Status = FileStatus.Ready
        };

        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        context.Media.AddRange(
            NewMedia("Batman 01", series.Id),
            NewMedia("Batman 02", series.Id));

        context.SaveChanges();
    }

    private static Media NewMedia(string name, string seriesId) => new()
    {
        Name = name,
        Path = $"/libros/Batman/{name}.cbz",
        Extension = "cbz",
        SeriesId = seriesId,
        Status = FileStatus.Ready,
        Pages = 20
    };

    [Fact]
    public async Task LibrariesFeed_LinksToTheLibraryFeed_SoNavigationIsNotBroken()
    {
        await using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root);

        var feed = await service.GetLibrariesFeedAsync(_owner, null);

        var link = Assert.Single(feed.Navigation);
        Assert.Equal($"/opds/v2.0/libraries/{_libraryId}", link.Href);
    }

    [Fact]
    public async Task LibraryFeed_ListsItsSeries()
    {
        await using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root);

        var feed = await service.GetLibrarySeriesFeedAsync(_owner, _libraryId, 0, null);

        Assert.NotNull(feed);
        Assert.Equal("Comics", feed!.Metadata.Title);

        var link = Assert.Single(feed.Navigation);
        Assert.Equal($"/opds/v2.0/series/{_seriesId}", link.Href);
    }

    [Fact]
    public async Task SeriesFeed_ListsItsBooks()
    {
        await using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root);

        var feed = await service.GetSeriesBooksFeedAsync(_owner, _seriesId, 0, null);

        Assert.NotNull(feed);
        Assert.Equal("Batman", feed!.Metadata.Title);
        Assert.Equal(2, feed.Publications.Count);
    }

    [Fact]
    public async Task UnknownIds_ReturnNull_SoTheEndpointCanRespond404()
    {
        await using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root);

        Assert.Null(await service.GetLibrarySeriesFeedAsync(_owner, "no-existe", 0, null));
        Assert.Null(await service.GetSeriesBooksFeedAsync(_owner, "no-existe", 0, null));
    }

    [Fact]
    public async Task AnExcludedLibraryIsNotReachableThroughItsOwnFeed()
    {
        await using var context = new DiarSpeicherDbContext(_options);
        var service = new OpdsV2Service(context, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root);

        var restricted = new AuthUser
        {
            Id = "lector",
            Username = "lector",
            IsServerOwner = false,
            ExcludedLibraryIds = [_libraryId]
        };

        Assert.Null(await service.GetLibrarySeriesFeedAsync(restricted, _libraryId, 0, null));
        Assert.Null(await service.GetSeriesBooksFeedAsync(restricted, _seriesId, 0, null));
    }
}
