using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class ForUserFilterTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private string _adultLibraryId = null!;

    public ForUserFilterTests()
    {
        _db = new TestSqliteDatabase();
        SeedTestData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedTestData()
    {
        var conn = _db.Connection;

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("Id", "DefaultReadingDir", "DefaultReadingMode", "LibraryPattern", "LibraryType")
            VALUES (@Id, 'LTR', 'Webtoon', 'Default', 'Manga');
            """;
        conn.Execute(insertConfigSql, new[] { new { Id = 1 }, new { Id = 2 } });

        var kidsLibId = Guid.NewGuid().ToString();
        var adultLibId = Guid.NewGuid().ToString();

        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'Ready', @ConfigId, @CreatedAt);
            """;
        conn.Execute(insertLibrarySql, new[]
        {
            new { Id = kidsLibId, Name = "Kids", Path = "/data/kids", ConfigId = 1, CreatedAt = DateTimeOffset.UtcNow },
            new { Id = adultLibId, Name = "Adults Only", Path = "/data/adults", ConfigId = 2, CreatedAt = DateTimeOffset.UtcNow }
        });

        // 1. Serie para niños con rating explícito en la serie = 8
        var kidsSeriesId = Guid.NewGuid().ToString();
        // 2. Serie sin metadata
        var unratedSeriesId = Guid.NewGuid().ToString();
        // 3. Serie para adultos
        var adultSeriesId = Guid.NewGuid().ToString();

        const string insertSeriesSql = """
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, @LibraryId, 'Ready', @CreatedAt);
            """;
        conn.Execute(insertSeriesSql, new[]
        {
            new { Id = kidsSeriesId, Name = "Donald Duck", Path = "/data/kids/dd", LibraryId = kidsLibId, CreatedAt = DateTimeOffset.UtcNow },
            new { Id = unratedSeriesId, Name = "Unrated Series", Path = "/data/kids/unrated", LibraryId = kidsLibId, CreatedAt = DateTimeOffset.UtcNow },
            new { Id = adultSeriesId, Name = "Berserk", Path = "/data/adults/berserk", LibraryId = adultLibId, CreatedAt = DateTimeOffset.UtcNow }
        });

        const string insertSeriesMetaSql = """
            INSERT INTO "SeriesMetadata" ("SeriesId", "AgeRating")
            VALUES (@SeriesId, @AgeRating);
            """;
        conn.Execute(insertSeriesMetaSql, new { SeriesId = kidsSeriesId, AgeRating = 8 });

        const string insertMediaSql = """
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Size", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'cbz', 1, @SeriesId, 'Ready', @CreatedAt);
            """;
        conn.Execute(insertMediaSql, new[]
        {
            new { Id = "m1", Name = "Donald 1.cbz", Path = "/data/kids/dd/1.cbz", SeriesId = kidsSeriesId, CreatedAt = DateTimeOffset.UtcNow },
            new { Id = "m2", Name = "Donald 2.cbz", Path = "/data/kids/dd/2.cbz", SeriesId = kidsSeriesId, CreatedAt = DateTimeOffset.UtcNow },
            new { Id = "m3", Name = "Unknown.cbz", Path = "/data/kids/unrated/1.cbz", SeriesId = unratedSeriesId, CreatedAt = DateTimeOffset.UtcNow },
            new { Id = "m4", Name = "Berserk 1.cbz", Path = "/data/adults/berserk/1.cbz", SeriesId = adultSeriesId, CreatedAt = DateTimeOffset.UtcNow }
        });

        const string insertMediaMetaSql = """
            INSERT INTO "MediaMetadata" ("MediaId", "Title", "AgeRating")
            VALUES (@MediaId, @Title, @AgeRating);
            """;
        conn.Execute(insertMediaMetaSql, new[]
        {
            new { MediaId = "m1", Title = "Donald 1", AgeRating = (int?)6 },
            new { MediaId = "m4", Title = "Berserk 1", AgeRating = (int?)18 }
        });

        _adultLibraryId = adultLibId;
    }

    private List<Media> GetVisibleMedia(AuthUser user)
    {
        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        var sql = $"""
            SELECT m."Id", m."Name", m."Path", m."Extension", m."Size", m."SeriesId",
                   s."Id", s."Name", s."Path", s."LibraryId",
                   mm."MediaId", mm."Title", mm."AgeRating"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."Id" ASC;
            """;

        return _db.Connection.Query<Media, Series, MediaMetadata, Media>(
            sql,
            (media, series, meta) =>
            {
                media.Series = series;
                media.Metadata = meta;
                return media;
            },
            parameters,
            splitOn: "Id,MediaId").ToList();
    }

    [Fact]
    public void ServerOwner_Sees_All_Media()
    {
        var owner = new AuthUser { Username = "admin", IsServerOwner = true };
        var books = GetVisibleMedia(owner);

        Assert.Equal(4, books.Count);
    }

    [Fact]
    public void User_With_Excluded_Library_Does_Not_See_Media_In_It()
    {
        var user = new AuthUser
        {
            Username = "user1",
            ExcludedLibraryIds = new HashSet<string> { _adultLibraryId }
        };

        var books = GetVisibleMedia(user);

        Assert.Equal(3, books.Count);
        Assert.DoesNotContain(books, b => b.Series != null && b.Series.LibraryId == _adultLibraryId);
    }

    [Fact]
    public void RestrictOnUnset_Allows_Explicit_And_Deferred_Ratings_Under_Max()
    {
        var childUser = new AuthUser
        {
            Username = "child",
            AgeRestriction = 10,
            RestrictOnUnset = true
        };

        var books = GetVisibleMedia(childUser);

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
    public void PermissiveMode_Allows_Unrated_Media_And_Series()
    {
        var teenUser = new AuthUser
        {
            Username = "teen",
            AgeRestriction = 14,
            RestrictOnUnset = false
        };

        var books = GetVisibleMedia(teenUser);

        // Donald 1 (6), Donald 2 (8), Unknown (unrated series allowed in permissive mode)
        // Berserk 1 (18 > 14) -> DENIED
        Assert.Equal(3, books.Count);
        Assert.DoesNotContain(books, b => b.Name == "Berserk 1.cbz");
    }
}
