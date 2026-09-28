using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class OpdsV2NavigationTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly AuthUser _owner = new() { Id = "admin", Username = "admin", IsServerOwner = true };

    private string _libraryId = null!;
    private string _seriesId = null!;

    public OpdsV2NavigationTests()
    {
        _db = new TestSqliteDatabase();
        Seed();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void Seed()
    {
        var conn = _db.Connection;

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("Id") VALUES (1);
            """;
        conn.Execute(insertConfigSql);

        var libId = Guid.NewGuid().ToString();
        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Comics', '/libros', 'Ready', 1, @Now);
            """;
        conn.Execute(insertLibrarySql, new { Id = libId, Now = DateTimeOffset.UtcNow });
        _libraryId = libId;

        var seriesId = Guid.NewGuid().ToString();
        const string insertSeriesSql = """
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Batman', '/libros/Batman', @LibraryId, 'Ready', @Now);
            """;
        conn.Execute(insertSeriesSql, new { Id = seriesId, LibraryId = libId, Now = DateTimeOffset.UtcNow });
        _seriesId = seriesId;

        const string insertMediaSql = """
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "SeriesId", "Status", "Pages", "Size", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'cbz', @SeriesId, 'Ready', 20, 1000, @Now);
            """;
        conn.Execute(insertMediaSql, new[]
        {
            new { Id = Guid.NewGuid().ToString(), Name = "Batman 01", Path = "/libros/Batman/Batman 01.cbz", SeriesId = seriesId, Now = DateTimeOffset.UtcNow },
            new { Id = Guid.NewGuid().ToString(), Name = "Batman 02", Path = "/libros/Batman/Batman 02.cbz", SeriesId = seriesId, Now = DateTimeOffset.UtcNow }
        });
    }

    [Fact]
    public async Task LibrariesFeed_LinksToTheLibraryFeed_SoNavigationIsNotBroken()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var feed = await service.GetLibrariesFeedAsync(_owner, null);

        var link = Assert.Single(feed.Navigation);
        Assert.Equal($"/opds/v2.0/libraries/{_libraryId}", link.Href);
    }

    [Fact]
    public async Task LibrarySeriesFeed_ListsTheSeriesInside_WithPagingMetadata()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var feed = await service.GetLibrarySeriesFeedAsync(_owner, _libraryId, 0, null);

        Assert.NotNull(feed);
        Assert.Equal("Comics", feed.Metadata.Title);
        Assert.Equal(1, feed.Metadata.NumberOfItems);

        var entry = Assert.Single(feed.Navigation);
        Assert.Equal($"/opds/v2.0/series/{_seriesId}", entry.Href);
        Assert.Equal("Batman", entry.Properties?["title"]);
    }

    [Fact]
    public async Task SeriesBooksFeed_ListsTheBooksInside_AsPublications()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var feed = await service.GetSeriesBooksFeedAsync(_owner, _seriesId, 0, null);

        Assert.NotNull(feed);
        Assert.Equal("Batman", feed.Metadata.Title);
        Assert.Equal(2, feed.Publications.Count);
        Assert.Contains(feed.Publications, p => p.Metadata.Title == "Batman 01");
        Assert.Contains(feed.Publications, p => p.Metadata.Title == "Batman 02");
    }

    [Fact]
    public async Task LibrarySeriesFeed_ReturnsNullForMissingLibrary()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var feed = await service.GetLibrarySeriesFeedAsync(_owner, "non-existent", 0, null);

        Assert.Null(feed);
    }

    [Fact]
    public async Task SeriesBooksFeed_ReturnsNullForMissingSeries()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var feed = await service.GetSeriesBooksFeedAsync(_owner, "non-existent", 0, null);

        Assert.Null(feed);
    }
}
