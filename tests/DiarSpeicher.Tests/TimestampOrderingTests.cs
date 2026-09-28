using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Komga;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

/// <summary>
/// Timestamps stay as ISO-8601 text, which is what SQLite writes for a DateTimeOffset.
/// The queries that need "newest first" are written with ORDER BY "CreatedAt" DESC.
/// </summary>
public sealed class TimestampOrderingTests : IDisposable
{
    private readonly TestSqliteDatabase _db;

    public TimestampOrderingTests()
    {
        _db = new TestSqliteDatabase();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TimestampsAreStoredAsSortableIsoText()
    {
        var created = new DateTimeOffset(2026, 9, 8, 14, 56, 59, TimeSpan.Zero);

        _db.Connection.Execute("""
            INSERT INTO "Users" ("Id", "Username", "CreatedAt")
            VALUES ('diar', 'diar', @CreatedAt);
            """, new { CreatedAt = created });

        using var command = _db.Connection.CreateCommand();
        command.CommandText = """SELECT typeof("CreatedAt"), "CreatedAt" FROM "Users" WHERE "Id" = 'diar'""";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));

        // Raw SQL orders on this text directly, so the format has to sort lexicographically:
        // zero-padded, most significant first, and a single UTC offset for every row.
        Assert.StartsWith("2026-09-08 14:56:59", reader.GetString(1), StringComparison.Ordinal);
        Assert.EndsWith("+00:00", reader.GetString(1), StringComparison.Ordinal);
    }

    [Fact]
    public void ATimestampSurvivesTheRoundTrip()
    {
        var created = new DateTimeOffset(2026, 9, 8, 14, 56, 59, 350, TimeSpan.Zero).AddTicks(3312);

        _db.Connection.Execute("""
            INSERT INTO "Users" ("Id", "Username", "CreatedAt")
            VALUES ('diar', 'diar', @CreatedAt);
            """, new { CreatedAt = created });

        var retrieved = _db.Connection.QuerySingle<DateTimeOffset>(
            """SELECT "CreatedAt" FROM "Users" WHERE "Id" = 'diar'""");

        Assert.Equal(created, retrieved);
    }

    [Fact]
    public async Task LatestBooks_ReturnsTheNewestFirst_AndPagesInTheDatabase()
    {
        var baseline = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        var configId = await _db.Connection.QuerySingleAsync<long>("""
            INSERT INTO "LibraryConfigs" DEFAULT VALUES RETURNING "Id";
            """);

        var libraryId = Guid.NewGuid().ToString();
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Comics', '/libros', 'Ready', @ConfigId, @Now);
            """, new { Id = libraryId, ConfigId = configId, Now = DateTimeOffset.UtcNow });

        var seriesId = Guid.NewGuid().ToString();
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Batman', '/libros/Batman', @LibraryId, 'Ready', @Now);
            """, new { Id = seriesId, LibraryId = libraryId, Now = DateTimeOffset.UtcNow });

        for (var i = 0; i < 5; i++)
        {
            await _db.Connection.ExecuteAsync("""
                INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "SeriesId", "Status", "CreatedAt")
                VALUES (@Id, @Name, @Path, 'cbz', @SeriesId, 'Ready', @CreatedAt);
                """, new
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"vol{i}",
                Path = $"/libros/Batman/vol{i}.cbz",
                SeriesId = seriesId,
                CreatedAt = baseline.AddMinutes(i)
            });
        }

        var service = new KomgaService(_db.Factory, NullLogger<KomgaService>.Instance, TestReadingProgress.For(_db.Factory));
        var owner = new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true };

        var first = await service.GetLatestBooksAsync(owner, page: 0, size: 2);
        Assert.Equal(5, first.TotalElements);
        Assert.Equal(["vol4", "vol3"], first.Content.Select(b => b.Name));

        var second = await service.GetLatestBooksAsync(owner, page: 1, size: 2);
        Assert.Equal(["vol2", "vol1"], second.Content.Select(b => b.Name));
    }

    /// <summary>
    /// Ulid is only monotonic across milliseconds: within the same millisecond the 80 random
    /// bits decide the order. A scan creates hundreds of media per millisecond, so ordering
    /// by id would shuffle them — which is why ordering goes through CreatedAt.
    /// </summary>
    [Fact]
    public void IdsAreNotAReliableSortKey()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => Ulid.NewUlid().ToString()).ToList();

        Assert.NotEqual(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
    }

    [Fact]
    public void ScannerAssignedIdsFitTheDeclaredColumnLength()
    {
        Assert.Equal(26, Ulid.NewUlid().ToString().Length);
        Assert.True(Ulid.NewUlid().ToString().Length <= 32);
    }
}
