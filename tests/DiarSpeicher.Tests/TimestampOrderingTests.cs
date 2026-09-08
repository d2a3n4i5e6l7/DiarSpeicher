using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Komga;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

/// <summary>
/// Timestamps stay as ISO-8601 text, which is what SQLite writes for a DateTimeOffset. EF
/// cannot translate an ORDER BY over that type, so the queries that need "newest first" are
/// written as raw SQL; these tests pin down both halves of that arrangement.
/// </summary>
public sealed class TimestampOrderingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    public TimestampOrderingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TimestampsAreStoredAsSortableIsoText()
    {
        using var db = new DiarSpeicherDbContext(_options);
        var created = new DateTimeOffset(2026, 9, 8, 14, 56, 59, TimeSpan.Zero);

        db.Users.Add(new User { Username = "diar", CreatedAt = created });
        db.SaveChanges();

        using var command = _connection.CreateCommand();
        command.CommandText = """SELECT typeof(CreatedAt), CreatedAt FROM Users""";
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

        using (var db = new DiarSpeicherDbContext(_options))
        {
            db.Users.Add(new User { Username = "diar", CreatedAt = created });
            db.SaveChanges();
        }

        using var verify = new DiarSpeicherDbContext(_options);

        Assert.Equal(created, verify.Users.Single().CreatedAt);
    }

    /// <summary>
    /// The reason the "newest first" queries are hand-written: EF refuses to translate this.
    /// If a future EF or provider starts supporting it, this test fails and the raw SQL can
    /// go back to LINQ.
    /// </summary>
    [Fact]
    public void EfStillCannotOrderByATimestamp()
    {
        using var db = new DiarSpeicherDbContext(_options);

        Assert.Throws<NotSupportedException>(
            () => db.Media.OrderByDescending(m => m.CreatedAt).ToQueryString());
    }

    [Fact]
    public async Task LatestBooks_ReturnsTheNewestFirst_AndPagesInTheDatabase()
    {
        var baseline = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        await using (var db = new DiarSpeicherDbContext(_options))
        {
            var library = new Library { Name = "Comics", Path = "/libros", Config = new LibraryConfig() };
            db.Libraries.Add(library);
            await db.SaveChangesAsync();

            var series = new Series { Name = "Batman", Path = "/libros/Batman", LibraryId = library.Id };
            db.Series.Add(series);
            await db.SaveChangesAsync();

            for (var i = 0; i < 5; i++)
            {
                db.Media.Add(new Media
                {
                    Name = $"vol{i}",
                    Path = $"/libros/Batman/vol{i}.cbz",
                    Extension = "cbz",
                    SeriesId = series.Id,
                    Status = FileStatus.Ready,
                    CreatedAt = baseline.AddMinutes(i)
                });
            }

            await db.SaveChangesAsync();
        }

        await using var context = new DiarSpeicherDbContext(_options);
        var service = new KomgaService(context, NullLogger<KomgaService>.Instance);
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
