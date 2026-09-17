using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Filesystem.Thumbnails;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

/// <summary>
/// Un disco que no esta montado deja su punto de montaje existiendo y vacio. Sin esta guarda,
/// el repaso del arranque veia cero series y marcaba la biblioteca entera como desaparecida.
/// </summary>
public sealed class EmptyLibraryGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _tempDir;

    public EmptyLibraryGuardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>().UseSqlite(_connection).Options;
        using var ctx = new DiarSpeicherDbContext(_options);
        ctx.Database.EnsureCreated();

        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_guard_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private LibraryScannerService NewScanner(DiarSpeicherDbContext db)
    {
        var processors = new IBookProcessor[] { new ZipBookProcessor() };
        var composite = new CompositeBookProcessor(processors);
        return new LibraryScannerService(
            db,
            new DirectoryScanner(),
            composite,
            new ThumbnailService(composite, NullLogger<ThumbnailService>.Instance),
            new ArchiveConversionService(NullLogger<ArchiveConversionService>.Instance),
            NullLogger<LibraryScannerService>.Instance);
    }

    private async Task<string> SeedLibraryWithSeriesAsync(string libDir)
    {
        using var ctx = new DiarSpeicherDbContext(_options);
        var libraryId = Guid.NewGuid().ToString();

        ctx.Libraries.Add(new Library
        {
            Id = libraryId,
            Name = "Montada",
            Path = libDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig { LibraryPattern = LibraryPattern.SeriesBased },
        });

        var serie = new Series
        {
            Id = Ulid.NewUlid().ToString(),
            Name = "Bleach",
            Path = Path.Combine(libDir, "Bleach"),
            LibraryId = libraryId,
            Status = FileStatus.Ready,
        };
        ctx.Series.Add(serie);
        ctx.Media.Add(new Media
        {
            Name = "vol01.cbz",
            Path = Path.Combine(serie.Path, "vol01.cbz"),
            Extension = "cbz",
            Pages = 20,
            SeriesId = serie.Id,
            Status = FileStatus.Ready,
        });

        await ctx.SaveChangesAsync();
        return libraryId;
    }

    [Fact]
    public async Task UnaCarpetaVaciaNoMarcaLaBibliotecaComoDesaparecida()
    {
        var libDir = Path.Combine(_tempDir, "montaje-ausente");
        Directory.CreateDirectory(libDir);          // existe, pero sin contenido
        var libraryId = await SeedLibraryWithSeriesAsync(libDir);

        using (var db = new DiarSpeicherDbContext(_options))
        {
            var report = await NewScanner(db).ScanLibraryAsync(libraryId);
            Assert.True(report.Success);
        }

        using var check = new DiarSpeicherDbContext(_options);
        Assert.All(await check.Series.ToListAsync(), s => Assert.Equal(FileStatus.Ready, s.Status));
        Assert.All(await check.Media.ToListAsync(), m => Assert.Equal(FileStatus.Ready, m.Status));
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
        using (var zip = System.IO.Compression.ZipFile.Open(libro, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var s = zip.CreateEntry("01.jpg").Open();
            s.Write([0xFF, 0xD8, 0xFF, 0xE0]);
        }

        using (var db = new DiarSpeicherDbContext(_options))
        {
            await NewScanner(db).ScanLibraryAsync(libraryId);
        }

        using var check = new DiarSpeicherDbContext(_options);
        var bleach = await check.Series.FirstAsync(s => s.Name == "Bleach");
        Assert.Equal(FileStatus.Missing, bleach.Status);
    }
}
