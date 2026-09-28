using Dapper;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using Xunit;

namespace DiarSpeicher.Tests;

/// <summary>
/// El orden se comprueba contra SQLite real: el fallo que motiva estos tests
/// —el tomo 10 delante del 2— nace de como el motor compara texto.
/// </summary>
public sealed class MediaSortOrderTests : IDisposable
{
    private readonly TestSqliteDatabase _db;

    public MediaSortOrderTests()
    {
        _db = new TestSqliteDatabase();
    }

    private void Seed(params string[] names)
    {
        var conn = _db.Connection;
        conn.Execute("""
            INSERT OR IGNORE INTO "Series" ("Id", "Name", "Path", "Status", "CreatedAt")
            VALUES ('series-1', 'Serie', '/s', 'Ready', @Now);
            """, new { Now = DateTimeOffset.UtcNow });

        foreach (var name in names)
        {
            conn.Execute("""
                INSERT INTO "Media" ("Id", "Name", "SortName", "Path", "Extension", "SeriesId", "Status", "CreatedAt")
                VALUES (@Id, @Name, @SortName, @Path, 'epub', 'series-1', 'Ready', @Now);
                """, new
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                SortName = SortKey.From(name),
                Path = $"/s/{name}",
                Now = DateTimeOffset.UtcNow
            });
        }
    }

    private List<string> OrderedNames()
    {
        const string sql = """
            SELECT "Name" FROM "Media"
            WHERE "SeriesId" = 'series-1'
            ORDER BY "SortName" ASC, "Name" ASC;
            """;
        return _db.Connection.Query<string>(sql).AsList();
    }

    [Fact]
    public void SqliteOrdersVolumesByNumber()
    {
        Seed(
            "Full Metal Panic! Volume 1",
            "Full Metal Panic! Volume 10",
            "Full Metal Panic! Volume 11",
            "Full Metal Panic! Volume 12",
            "Full Metal Panic! Volume 2",
            "Full Metal Panic! Volume 3",
            "Full Metal Panic! Volume 4",
            "Full Metal Panic! Volume 5",
            "Full Metal Panic! Volume 6",
            "Full Metal Panic! Volume 7",
            "Full Metal Panic! Volume 8",
            "Full Metal Panic! Volume 9");

        Assert.Equal(
            [
                "Full Metal Panic! Volume 1",
                "Full Metal Panic! Volume 2",
                "Full Metal Panic! Volume 3",
                "Full Metal Panic! Volume 4",
                "Full Metal Panic! Volume 5",
                "Full Metal Panic! Volume 6",
                "Full Metal Panic! Volume 7",
                "Full Metal Panic! Volume 8",
                "Full Metal Panic! Volume 9",
                "Full Metal Panic! Volume 10",
                "Full Metal Panic! Volume 11",
                "Full Metal Panic! Volume 12"
            ],
            OrderedNames());
    }

    [Fact]
    public void DifferentWordingSortsAlphabeticallyAndTheNumberOnlyBreaksTies()
    {
        Seed("aaa 2", "aaa 10", "aaa 1", "zzz 1");

        Assert.Equal(["aaa 1", "aaa 2", "aaa 10", "zzz 1"], OrderedNames());
    }

    [Fact]
    public async Task BackfillFillsTheKeyOfRowsThatPredateTheColumn()
    {
        var conn = _db.Connection;
        conn.Execute("""
            INSERT OR IGNORE INTO "Series" ("Id", "Name", "Path", "Status", "CreatedAt")
            VALUES ('series-1', 'Serie', '/s', 'Ready', @Now);
            """, new { Now = DateTimeOffset.UtcNow });

        foreach (var name in new[] { "Vol 10", "Vol 2", "Vol 1" })
        {
            conn.Execute("""
                INSERT INTO "Media" ("Id", "Name", "SortName", "Path", "Extension", "SeriesId", "Status", "CreatedAt")
                VALUES (@Id, @Name, NULL, @Path, 'epub', 'series-1', 'Ready', @Now);
                """, new
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Path = $"/s/{name}",
                Now = DateTimeOffset.UtcNow
            });
        }

        var filled = await _db.Factory.BackfillSortNamesAsync();
        Assert.Equal(3, filled);

        Assert.Equal(["Vol 1", "Vol 2", "Vol 10"], OrderedNames());
    }

    [Fact]
    public async Task BackfillIsIdempotent()
    {
        Seed("Vol 1", "Vol 2");

        Assert.Equal(0, await _db.Factory.BackfillSortNamesAsync());
    }

    public void Dispose() => _db.Dispose();
}
