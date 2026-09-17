using System.Reflection;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

public sealed class ScannerScalingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _tempDir;

    public ScannerScalingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>().UseSqlite(_connection).Options;
        using var ctx = new DiarSpeicherDbContext(_options);
        ctx.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_scale_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string? BuscarDueno(Dictionary<string, string> owners, string directorio)
    {
        var metodo = typeof(LibraryScannerService)
            .GetMethod("FindOwningSeries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string?)metodo.Invoke(null, [owners, directorio]);
    }

    private static Dictionary<string, string> Duenos(params (string Ruta, string Id)[] series)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (ruta, id) in series) d[Path.GetFullPath(ruta)] = id;
        return d;
    }

    [Fact]
    public void ElDuenoEsLaSerieMasCercanaPorEncima()
    {
        var owners = Duenos(
            ("/lib/Bleach", "raiz"),
            ("/lib/Bleach/Arcos", "hondo"));

        Assert.Equal("hondo", BuscarDueno(owners, "/lib/Bleach/Arcos/Soul Society"));
        Assert.Equal("hondo", BuscarDueno(owners, "/lib/Bleach/Arcos"));
        Assert.Equal("raiz", BuscarDueno(owners, "/lib/Bleach"));
    }

    [Fact]
    public void UnaSerieConNombreParecidoNoSeLlevaLosFicherosDeOtra()
    {
        var owners = Duenos(("/lib/Bleach", "bleach"));

        Assert.Null(BuscarDueno(owners, "/lib/Bleach2"));
        Assert.Null(BuscarDueno(owners, "/lib/BleachRemastered/vol01"));
    }

    [Fact]
    public void UnDirectorioFueraDeTodaSerieNoTieneDueno()
    {
        var owners = Duenos(("/lib/Bleach", "bleach"));

        Assert.Null(BuscarDueno(owners, "/otro/sitio"));
        Assert.Null(BuscarDueno(owners, "/"));
    }

    [Fact]
    public void LaBarraFinalNoCambiaElResultado()
    {
        var owners = Duenos(("/lib/Bleach", "bleach"));

        Assert.Equal("bleach", BuscarDueno(owners, "/lib/Bleach/"));
        Assert.Equal("bleach", BuscarDueno(owners, "/lib/Bleach"));
    }

    private LibraryScannerService NewScanner(DiarSpeicherDbContext db)
    {
        var composite = new CompositeBookProcessor([new ZipBookProcessor()]);
        return new LibraryScannerService(
            db,
            new DirectoryScanner(),
            composite,
            new ThumbnailService(composite, NullLogger<ThumbnailService>.Instance),
            new ArchiveConversionService(NullLogger<ArchiveConversionService>.Instance),
            NullLogger<LibraryScannerService>.Instance);
    }

    private async Task<(string LibraryId, List<string> SeriesIds)> SembrarAsync(int series, int tomosPorSerie)
    {
        using var ctx = new DiarSpeicherDbContext(_options);
        var libraryId = Guid.NewGuid().ToString();
        var libDir = Path.Combine(_tempDir, "lib");
        Directory.CreateDirectory(libDir);

        ctx.Libraries.Add(new Library
        {
            Id = libraryId,
            Name = "Grande",
            Path = libDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig { LibraryPattern = LibraryPattern.SeriesBased },
        });

        var ids = new List<string>();
        for (var s = 0; s < series; s++)
        {
            var serie = new Series
            {
                Id = Ulid.NewUlid().ToString(),
                Name = $"Serie{s}",
                Path = Path.Combine(libDir, $"Serie{s}"),
                LibraryId = libraryId,
                Status = FileStatus.Ready,
            };
            ctx.Series.Add(serie);
            ids.Add(serie.Id);

            for (var v = 0; v < tomosPorSerie; v++)
            {
                ctx.Media.Add(new Media
                {
                    Name = $"vol{v:D2}.cbz",
                    Path = Path.Combine(serie.Path, $"vol{v:D2}.cbz"),
                    Extension = "cbz",
                    Pages = 10,
                    SeriesId = serie.Id,
                    Status = FileStatus.Ready,
                });
            }
        }

        await ctx.SaveChangesAsync();
        return (libraryId, ids);
    }

    [Fact]
    public async Task MarcarSeriesDesaparecidasNoConsultaUnaVezPorSerie()
    {
        var (libraryId, _) = await SembrarAsync(series: 40, tomosPorSerie: 5);

        using var db = new DiarSpeicherDbContext(_options);
        var rutas = await db.Series.Where(s => s.LibraryId == libraryId).Select(s => s.Path).ToListAsync();

        var metodo = typeof(LibraryScannerService)
            .GetMethod("ProcessMissingSeriesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var report = new LibraryScanReport();
        await (Task)metodo.Invoke(NewScanner(db), [libraryId, rutas, report, CancellationToken.None])!;

        using var check = new DiarSpeicherDbContext(_options);
        Assert.Equal(40, await check.Series.CountAsync(s => s.Status == FileStatus.Missing));
        Assert.Equal(200, await check.Media.CountAsync(m => m.Status == FileStatus.Missing));
        Assert.Equal(200ul, report.UpdatedMedia);
    }

    [Fact]
    public async Task LaReconciliacionSueltaLosMediosDelRastreador()
    {
        var (libraryId, _) = await SembrarAsync(series: 10, tomosPorSerie: 20);

        using var db = new DiarSpeicherDbContext(_options);
        var metodo = typeof(LibraryScannerService)
            .GetMethod("ReconcileMediaOwnershipAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await (Task)metodo.Invoke(NewScanner(db), [libraryId, new LibraryScanReport(), CancellationToken.None])!;

        Assert.Empty(db.ChangeTracker.Entries<Media>());
    }

    [Fact]
    public async Task LaReconciliacionNoDesengarchaLaBibliotecaQueElEscaneoActualizaDespues()
    {
        var (libraryId, _) = await SembrarAsync(series: 3, tomosPorSerie: 2);

        using var db = new DiarSpeicherDbContext(_options);
        var library = await db.Libraries.FirstAsync(l => l.Id == libraryId);

        var metodo = typeof(LibraryScannerService)
            .GetMethod("ReconcileMediaOwnershipAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)metodo.Invoke(NewScanner(db), [libraryId, new LibraryScanReport(), CancellationToken.None])!;

        library.LastScannedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        using var check = new DiarSpeicherDbContext(_options);
        Assert.NotNull((await check.Libraries.FirstAsync(l => l.Id == libraryId)).LastScannedAt);
    }
}
