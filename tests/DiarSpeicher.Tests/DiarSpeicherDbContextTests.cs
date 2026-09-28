using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly TestSqliteDatabase _db;

    public SqliteDatabaseTests()
    {
        _db = new TestSqliteDatabase();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Can_Insert_And_Retrieve_Library_With_Series_And_Media()
    {
        var conn = _db.Connection;

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("ConvertRarToZip", "DefaultReadingDir", "DefaultReadingMode", "LibraryPattern", "LibraryType")
            VALUES (0, 'LTR', 'Webtoon', 'SeriesBased', 'Comic');
            SELECT last_insert_rowid();
            """;
        var configId = await conn.ExecuteScalarAsync<int>(insertConfigSql);

        var libraryId = Guid.NewGuid().ToString();
        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'Ready', @ConfigId, @CreatedAt);
            """;
        await conn.ExecuteAsync(insertLibrarySql, new
        {
            Id = libraryId,
            Name = "Comics Library",
            Path = "/data/comics",
            ConfigId = configId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var seriesId = Guid.NewGuid().ToString();
        const string insertSeriesSql = """
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, @LibraryId, 'Ready', @CreatedAt);
            """;
        await conn.ExecuteAsync(insertSeriesSql, new
        {
            Id = seriesId,
            Name = "Batman (2016)",
            Path = "/data/comics/Batman",
            LibraryId = libraryId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        const string insertSeriesMetaSql = """
            INSERT INTO "SeriesMetadata" ("SeriesId", "Publisher", "Year")
            VALUES (@SeriesId, 'DC Comics', 2016);
            """;
        await conn.ExecuteAsync(insertSeriesMetaSql, new { SeriesId = seriesId });

        var mediaId = Guid.NewGuid().ToString();
        const string insertMediaSql = """
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Size", "Pages", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'cbz', 1048576, 32, @SeriesId, 'Ready', @CreatedAt);
            """;
        await conn.ExecuteAsync(insertMediaSql, new
        {
            Id = mediaId,
            Name = "Batman #01.cbz",
            Path = "/data/comics/Batman/Batman #01.cbz",
            SeriesId = seriesId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        const string querySql = """
            SELECT l."Id", l."Name", s."Id", s."Name", sm."SeriesId", sm."Publisher", m."Id", m."Name"
            FROM "Libraries" l
            JOIN "Series" s ON s."LibraryId" = l."Id"
            JOIN "SeriesMetadata" sm ON sm."SeriesId" = s."Id"
            JOIN "Media" m ON m."SeriesId" = s."Id"
            WHERE l."Id" = @Id;
            """;

        var retrieved = await conn.QueryAsync<Library, Series, SeriesMetadata, Media, (Library, Series, SeriesMetadata, Media)>(
            querySql,
            (l, s, sm, m) => (l, s, sm, m),
            new { Id = libraryId },
            splitOn: "Id,SeriesId,Id");

        var first = retrieved.FirstOrDefault();
        Assert.NotNull(first.Item1);
        Assert.Equal("Comics Library", first.Item1.Name);
        Assert.Equal("Batman (2016)", first.Item2.Name);
        Assert.Equal("DC Comics", first.Item3.Publisher);
        Assert.Equal("Batman #01.cbz", first.Item4.Name);
    }

    [Fact]
    public async Task Cascade_Delete_Works_When_Library_Is_Deleted()
    {
        var conn = _db.Connection;

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("ConvertRarToZip", "DefaultReadingDir", "DefaultReadingMode", "LibraryPattern", "LibraryType")
            VALUES (0, 'LTR', 'Webtoon', 'SeriesBased', 'Comic');
            SELECT last_insert_rowid();
            """;
        var configId = await conn.ExecuteScalarAsync<int>(insertConfigSql);

        var libraryId = Guid.NewGuid().ToString();
        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'Ready', @ConfigId, @CreatedAt);
            """;
        await conn.ExecuteAsync(insertLibrarySql, new
        {
            Id = libraryId,
            Name = "To Delete",
            Path = "/data/delete",
            ConfigId = configId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var seriesId = Guid.NewGuid().ToString();
        const string insertSeriesSql = """
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, @Name, @Path, @LibraryId, 'Ready', @CreatedAt);
            """;
        await conn.ExecuteAsync(insertSeriesSql, new
        {
            Id = seriesId,
            Name = "Series",
            Path = "/data/delete/Series",
            LibraryId = libraryId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        const string deleteLibrarySql = """DELETE FROM "Libraries" WHERE "Id" = @Id;""";
        await conn.ExecuteAsync(deleteLibrarySql, new { Id = libraryId });

        const string countSeriesSql = """SELECT COUNT(*) FROM "Series" WHERE "Id" = @Id;""";
        var seriesCount = await conn.ExecuteScalarAsync<int>(countSeriesSql, new { Id = seriesId });
        Assert.Equal(0, seriesCount);
    }
}
