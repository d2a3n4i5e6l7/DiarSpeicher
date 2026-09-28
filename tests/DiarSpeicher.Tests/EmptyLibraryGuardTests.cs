using System.IO.Compression;
using Dapper;
using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

/// <summary>
/// Un disco que no esta montado deja su punto de montaje existiendo y vacio. Sin esta guarda,
/// el repaso del arranque veia cero series y marcaba la biblioteca entera como desaparecida.
/// </summary>
public sealed class EmptyLibraryGuardTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _tempDir;

    public EmptyLibraryGuardTests()
    {
        _db = new TestSqliteDatabase();
        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_guard_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private LibraryScannerService NewScanner()
    {
        var processors = new IBookProcessor[] { new ZipBookProcessor() };
        var composite = new CompositeBookProcessor(processors);
        return new LibraryScannerService(
            _db.Factory,
            new DirectoryScanner(),
            composite,
            new ThumbnailService(composite, NullLogger<ThumbnailService>.Instance),
            new ArchiveConversionService(NullLogger<ArchiveConversionService>.Instance),
            NullLogger<LibraryScannerService>.Instance);
    }

    private async Task<string> SeedLibraryWithSeriesAsync(string libDir)
    {
        var configId = await _db.Connection.QuerySingleAsync<long>("""
            INSERT INTO "LibraryConfigs" ("LibraryPattern") VALUES ('SeriesBased') RETURNING "Id";
            """);

        var libraryId = Guid.NewGuid().ToString();
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Montada', @Path, 'Ready', @ConfigId, @Now);
            """, new { Id = libraryId, Path = libDir, ConfigId = configId, Now = DateTimeOffset.UtcNow });

        var serieId = Ulid.NewUlid().ToString();
        var seriePath = Path.Combine(libDir, "Bleach");
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
            VALUES (@Id, 'Bleach', @Path, @LibraryId, 'Ready', @Now);
            """, new { Id = serieId, Path = seriePath, LibraryId = libraryId, Now = DateTimeOffset.UtcNow });

        var mediaId = Guid.NewGuid().ToString();
        await _db.Connection.ExecuteAsync("""
            INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Pages", "SeriesId", "Status", "CreatedAt")
            VALUES (@Id, 'vol01.cbz', @Path, 'cbz', 20, @SeriesId, 'Ready', @Now);
            """, new
        {
            Id = mediaId,
            Path = Path.Combine(seriePath, "vol01.cbz"),
            SeriesId = serieId,
            Now = DateTimeOffset.UtcNow
        });

        return libraryId;
    }

    [Fact]
    public async Task UnaCarpetaVaciaNoMarcaLaBibliotecaComoDesaparecida()
    {
        var libDir = Path.Combine(_tempDir, "montaje-ausente");
        Directory.CreateDirectory(libDir);          // existe, pero sin contenido
        var libraryId = await SeedLibraryWithSeriesAsync(libDir);

        var report = await NewScanner().ScanLibraryAsync(libraryId);
        Assert.True(report.Success);

        var allSeries = (await _db.Connection.QueryAsync<Series>("""SELECT * FROM "Series" """)).ToList();
        Assert.All(allSeries, s => Assert.Equal(FileStatus.Ready, s.Status));

        var allMedia = (await _db.Connection.QueryAsync<Media>("""SELECT * FROM "Media" """)).ToList();
        Assert.All(allMedia, m => Assert.Equal(FileStatus.Ready, m.Status));
    }

    [Fact]
    public async Task UnaSerieBorradaDeVerdadSiSeMarca()
    {
        var libDir = Path.Combine(_tempDir, "montaje-presente");
        Directory.CreateDirectory(libDir);
        var libraryId = await SeedLibraryWithSeriesAsync(libDir);

        // Otra serie sigue en disco, asi que el disco NO viene vacio: la que falta, falta.
        var viva = Path.Combine(libDir, "Naruto");
        Directory.CreateDirectory(viva);
        var libro = Path.Combine(viva, "vol01.cbz");
        using (var zip = ZipFile.Open(libro, ZipArchiveMode.Create))
        {
            using var s = zip.CreateEntry("01.jpg").Open();
            s.Write([0xFF, 0xD8, 0xFF, 0xE0]);
        }

        await NewScanner().ScanLibraryAsync(libraryId);

        var bleach = await _db.Connection.QueryFirstAsync<Series>(
            """SELECT * FROM "Series" WHERE "Name" = 'Bleach'""");
        Assert.Equal(FileStatus.Missing, bleach.Status);
    }
}
