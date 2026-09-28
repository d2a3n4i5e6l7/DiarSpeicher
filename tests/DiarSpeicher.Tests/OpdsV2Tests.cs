using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Infrastructure.Opds;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class OpdsV2Tests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private string _kidBookId = null!;
    private string _adultBookId = null!;

    public OpdsV2Tests()
    {
        _db = new TestSqliteDatabase();
        SeedTestData();
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("Id") VALUES (1);
            """;
        conn.Execute(insertConfigSql);

        var libId = Guid.NewGuid().ToString();
        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Manga & Comics', '/data/comics', 'Ready', 1, @Now);
            """;
        conn.Execute(insertLibrarySql, new { Id = libId, Now = DateTimeOffset.UtcNow });

        var seriesId = Guid.NewGuid().ToString();
        const string insertSeriesSql = """
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'One Piece', '/data/comics/op', @LibraryId, 'Ready', @Now);
            """;
        conn.Execute(insertSeriesSql, new { Id = seriesId, LibraryId = libId, Now = DateTimeOffset.UtcNow });

        var kidBookId = Guid.NewGuid().ToString();
        var adultBookId = Guid.NewGuid().ToString();

        const string insertMediaSql = """
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "Size", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'cbz', @Pages, @Size, @SeriesId, 'Ready', @Now);
            """;
        conn.Execute(insertMediaSql, new[]
        {
            new { Id = kidBookId, Name = "One Piece Vol 01", Path = "/data/comics/op/01.cbz", Pages = 200, Size = 50000000L, SeriesId = seriesId, Now = DateTimeOffset.UtcNow },
            new { Id = adultBookId, Name = "Berserk Vol 01", Path = "/data/comics/op/b01.cbz", Pages = 220, Size = 60000000L, SeriesId = seriesId, Now = DateTimeOffset.UtcNow }
        });

        const string insertMetaSql = """
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "AgeRating")
            VALUES (@MediaId, @Title, @AgeRating);
            """;
        conn.Execute(insertMetaSql, new[]
        {
            new { MediaId = kidBookId, Title = "Romance Dawn", AgeRating = 10 },
            new { MediaId = adultBookId, Title = "The Black Swordsman", AgeRating = 18 }
        });

        const string insertUserSql = """
            INSERT INTO "Users" ("Id", "Username", "CreatedAt")
            VALUES ('reader_v2', 'luffy', @Now);
            """;
        conn.Execute(insertUserSql, new { Now = DateTimeOffset.UtcNow });

        _kidBookId = kidBookId;
        _adultBookId = adultBookId;
    }

    [Fact]
    public void GetAuthenticationDoc_ReturnsValidAuthDocument()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var authDoc = service.GetAuthenticationDoc(null);

        Assert.NotNull(authDoc);
        Assert.Contains(authDoc.Authentication, flow => flow.Type == "http://opds-spec.org/auth/basic");
        Assert.NotEmpty(authDoc.Links);
    }

    [Fact]
    public async Task GetCatalogFeedAsync_ReturnsJsonLdFeedWithNavigationAndGroups()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

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
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };
        var pub = await service.GetPublicationAsync(user, _kidBookId, null);

        Assert.NotNull(pub);
        Assert.Equal("Romance Dawn", pub.Metadata.Title);
        Assert.Equal(200, pub.Metadata.NumberOfPages);
        Assert.NotEmpty(pub.Images);
        Assert.NotEmpty(pub.Links);

        Assert.Equal(200, pub.ReadingOrder.Count);
        Assert.Equal("/opds/v2.0/books/" + _kidBookId + "/pages/0?zero_based=true", pub.ReadingOrder[0].Href);
    }

    [Fact]
    public async Task Progression_CanUpdateAndRetrieveProgress()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

        var user = new AuthUser { Id = "reader_v2", Username = "luffy", IsServerOwner = false };

        var updateSuccess = await service.UpdateProgressionAsync(user, _kidBookId, new OpdsV2Progression
        {
            Page = 45,
            Percentage = 0.225m
        });

        Assert.True(updateSuccess);

        var prog = await service.GetProgressionAsync(user, _kidBookId);
        Assert.NotNull(prog);
        Assert.Equal(45, prog.Page);
        Assert.Equal(0.225m, prog.Percentage);

        const string sessionSql = """SELECT * FROM "ReadingSessions" WHERE "MediaId" = @MediaId AND "UserId" = @UserId LIMIT 1;""";
        var session = await _db.Connection.QueryFirstOrDefaultAsync<ReadingSession>(sessionSql, new { MediaId = _kidBookId, UserId = "reader_v2" });
        Assert.NotNull(session);
        Assert.Equal(45, session.EndPage);
        Assert.Equal(ReadingStatus.Reading, session.Status);
    }

    [Fact]
    public async Task GetBooksFeedAsync_EnforcesParentalAgeRestrictionInOpdsV2()
    {
        var service = new OpdsV2Service(_db.Factory, NullLogger<OpdsV2Service>.Instance, TestLinkPrefix.Root, TestReadingProgress.For(_db.Factory));

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
        _db.Dispose();
    }
}
