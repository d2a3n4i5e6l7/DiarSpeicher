using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Tests;

/// <summary>
/// El orden se comprueba contra SQLite real y no en memoria: el fallo que motiva estos tests
/// —el tomo 10 delante del 2— nace de como el motor compara texto, y un proveedor en memoria
/// ordenaria con las reglas de .NET, que no son las mismas.
/// </summary>
public sealed class MediaSortOrderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    public MediaSortOrderTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();
    }

    private void Seed(params string[] names)
    {
        using var context = new DiarSpeicherDbContext(_options);
        context.Series.Add(new Series { Id = "series-1", Name = "Serie", Path = "/s" });

        foreach (var name in names)
        {
            context.Media.Add(new Media
            {
                Name = name,
                SortName = SortKey.From(name),
                Path = $"/s/{name}",
                Extension = "epub",
                SeriesId = "series-1"
            });
        }

        context.SaveChanges();
    }

    private List<string> OrderedNames()
    {
        using var context = new DiarSpeicherDbContext(_options);
        return [.. context.Media
            .Where(m => m.SeriesId == "series-1")
            .OrderBy(m => m.SortName).ThenBy(m => m.Name)
            .Select(m => m.Name)];
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

    /// <summary>
    /// Con titulos distintos manda el texto, como en cualquier listado alfabetico; el numero
    /// solo desempata entre nombres que comparten el mismo texto, que es el caso de una serie.
    /// </summary>
    [Fact]
    public void DifferentWordingSortsAlphabeticallyAndTheNumberOnlyBreaksTies()
    {
        Seed("aaa 2", "aaa 10", "aaa 1", "zzz 1");

        Assert.Equal(["aaa 1", "aaa 2", "aaa 10", "zzz 1"], OrderedNames());
    }

    [Fact]
    public async Task BackfillFillsTheKeyOfRowsThatPredateTheColumn()
    {
        using (var context = new DiarSpeicherDbContext(_options))
        {
            context.Series.Add(new Series { Id = "series-1", Name = "Serie", Path = "/s" });
            foreach (var name in new[] { "Vol 10", "Vol 2", "Vol 1" })
            {
                context.Media.Add(new Media
                {
                    Name = name,
                    SortName = null,
                    Path = $"/s/{name}",
                    Extension = "epub",
                    SeriesId = "series-1"
                });
            }

            context.SaveChanges();
        }

        using (var context = new DiarSpeicherDbContext(_options))
        {
            var filled = await context.BackfillSortNamesAsync();
            Assert.Equal(3, filled);
        }

        Assert.Equal(["Vol 1", "Vol 2", "Vol 10"], OrderedNames());
    }

    [Fact]
    public async Task BackfillIsIdempotent()
    {
        Seed("Vol 1", "Vol 2");

        using var context = new DiarSpeicherDbContext(_options);
        Assert.Equal(0, await context.BackfillSortNamesAsync());
    }

    public void Dispose() => _connection.Dispose();
}
