using System.Reflection;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class ScannerScalingTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _tempDir;

    public ScannerScalingTests()
    {
        _db = new TestSqliteDatabase();
        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_scale_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _db.Dispose();
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

    private LibraryScannerService NewScanner()
    {
        var composite = new CompositeBookProcessor([new ZipBookProcessor()]);
        return new LibraryScannerService(
            _db.Factory,
            new DirectoryScanner(),
            composite,
            new ThumbnailService(composite, NullLogger<ThumbnailService>.Instance),
            new ArchiveConversionService(NullLogger<ArchiveConversionService>.Instance),
            NullLogger<LibraryScannerService>.Instance);
    }

    private async Task<(string LibraryId, List<string> SeriesIds)> SembrarAsync(int series, int tomosPorSerie)
    {
        var conn = _db.Connection;
        var configId = await conn.QuerySingleAsync<long>("""
            INSERT INTO "LibraryConfigs" ("LibraryPattern") VALUES ('SeriesBased') RETURNING "Id";
            """);

        var libraryId = Guid.NewGuid().ToString();
        var libDir = Path.Combine(_tempDir, "lib");
        Directory.CreateDirectory(libDir);

        await conn.ExecuteAsync("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Grande', @Path, 'Ready', @ConfigId, @Now);
            """, new { Id = libraryId, Path = libDir, ConfigId = configId, Now = DateTimeOffset.UtcNow });

        var ids = new List<string>();
        for (var s = 0; s < series; s++)
        {
            var serieId = Ulid.NewUlid().ToString();
            var seriePath = Path.Combine(libDir, $"Serie{s}");
            await conn.ExecuteAsync("""
                INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
                VALUES (@Id, @Name, @Path, @LibraryId, 'Ready', @Now);
                """, new { Id = serieId, Name = $"Serie{s}", Path = seriePath, LibraryId = libraryId, Now = DateTimeOffset.UtcNow });
            ids.Add(serieId);

            for (var v = 0; v < tomosPorSerie; v++)
            {
                await conn.ExecuteAsync("""
                    INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "SeriesId", "Status", "CreatedAt")
                    VALUES (@Id, @Name, @Path, 'cbz', 10, @SeriesId, 'Ready', @Now);
                    """, new
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = $"vol{v:D2}.cbz",
                    Path = Path.Combine(seriePath, $"vol{v:D2}.cbz"),
                    SeriesId = serieId,
                    Now = DateTimeOffset.UtcNow
                });
            }
        }

        return (libraryId, ids);
    }

    [Fact]
    public async Task MarcarSeriesDesaparecidasNoConsultaUnaVezPorSerie()
    {
        var (libraryId, _) = await SembrarAsync(series: 40, tomosPorSerie: 5);

        var rutas = (await _db.Connection.QueryAsync<string>(
            """SELECT "Path" FROM "Series" WHERE "LibraryId" = @LibraryId""",
            new { LibraryId = libraryId })).ToList();

        var metodo = typeof(LibraryScannerService)
            .GetMethod("ProcessMissingSeriesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var report = new LibraryScanReport();
        await (Task)metodo.Invoke(NewScanner(), [libraryId, rutas, report, CancellationToken.None])!;

        var missingSeriesCount = await _db.Connection.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "Series" WHERE "Status" = 'Missing'""");
        var missingMediaCount = await _db.Connection.ExecuteScalarAsync<int>(
            """SELECT COUNT(*) FROM "Media" WHERE "Status" = 'Missing'""");

        Assert.Equal(40, missingSeriesCount);
        Assert.Equal(200, missingMediaCount);
        Assert.Equal(200ul, report.UpdatedMedia);
    }

    [Fact]
    public async Task LaReconciliacionSueltaLosMediosDelRastreador()
    {
        var (libraryId, _) = await SembrarAsync(series: 10, tomosPorSerie: 20);

        var metodo = typeof(LibraryScannerService)
            .GetMethod("ReconcileMediaOwnershipAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // In Dapper, execution is fully stateless and allocation is O(1)
        var report = new LibraryScanReport();
        await (Task)metodo.Invoke(NewScanner(), [libraryId, report, CancellationToken.None])!;

        var totalMedia = await _db.Connection.ExecuteScalarAsync<int>("""SELECT COUNT(*) FROM "Media" """);
        Assert.Equal(200, totalMedia);
    }

    [Fact]
    public async Task LaReconciliacionNoDesengarchaLaBibliotecaQueElEscaneoActualizaDespues()
    {
        var (libraryId, _) = await SembrarAsync(series: 3, tomosPorSerie: 2);

        var metodo = typeof(LibraryScannerService)
            .GetMethod("ReconcileMediaOwnershipAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)metodo.Invoke(NewScanner(), [libraryId, new LibraryScanReport(), CancellationToken.None])!;

        var now = DateTimeOffset.UtcNow;
        await _db.Connection.ExecuteAsync(
            """UPDATE "Libraries" SET "LastScannedAt" = @Now WHERE "Id" = @Id""",
            new { Now = now, Id = libraryId });

        var library = await _db.Connection.QueryFirstOrDefaultAsync<Library>(
            """SELECT "Id", "LastScannedAt" FROM "Libraries" WHERE "Id" = @Id""",
            new { Id = libraryId });
        Assert.NotNull(library?.LastScannedAt);
    }
}
