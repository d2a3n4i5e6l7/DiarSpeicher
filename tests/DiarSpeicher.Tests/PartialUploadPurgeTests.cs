using System.Text.Json;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Metadata;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Tests;

public sealed class PartialUploadPurgeTests : IDisposable
{
    private readonly string _root;
    private readonly string _uploads;
    private readonly string _dbDir;
    private readonly string _libreria;

    public PartialUploadPurgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"diar_purge_{Guid.NewGuid():N}");
        _uploads = Path.Combine(_root, "cache", "uploads");
        _dbDir = Path.Combine(_root, "manga_database");
        _libreria = Path.Combine(_root, "biblioteca");
        Directory.CreateDirectory(_uploads);
        Directory.CreateDirectory(_dbDir);
        Directory.CreateDirectory(_libreria);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private PartialUploadPurgeService Servicio(int horas = 24)
    {
        var storage = new StorageOptions { RootPath = _root };
        storage.Upload.AbandonedUploadHours = horas;

        return new PartialUploadPurgeService(
            Options.Create(storage),
            Options.Create(new MangaBakaOptions { DatabasePath = _dbDir }),
            NullLogger<PartialUploadPurgeService>.Instance);
    }

    private static void Envejecer(string path, TimeSpan edad) =>
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - edad);

    private async Task<(string Meta, string Part)> CrearSubidaAsync(string id, TimeSpan edad)
    {
        var part = Path.Combine(_libreria, $".tomo.{id}.part");
        await File.WriteAllBytesAsync(part, new byte[1024]);

        var meta = Path.Combine(_uploads, $"{id}.meta");
        await File.WriteAllTextAsync(meta, JsonSerializer.Serialize(new { UploadId = id, PartPath = part }));

        Envejecer(part, edad);
        Envejecer(meta, edad);
        return (meta, part);
    }

    private static async Task BarrerAsync(PartialUploadPurgeService servicio)
    {
        using var cts = new CancellationTokenSource();
        await servicio.StartAsync(cts.Token);
        await Task.Delay(150, CancellationToken.None);
        await cts.CancelAsync();
        await servicio.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnaSubidaAbandonadaSeBarreConSuParcial()
    {
        var (meta, part) = await CrearSubidaAsync("vieja", TimeSpan.FromHours(30));

        await BarrerAsync(Servicio());

        Assert.False(File.Exists(part));
        Assert.False(File.Exists(meta));
    }

    [Fact]
    public async Task UnaSubidaRecienteNoSeToca()
    {
        var (meta, part) = await CrearSubidaAsync("nueva", TimeSpan.FromHours(2));

        await BarrerAsync(Servicio());

        Assert.True(File.Exists(part));
        Assert.True(File.Exists(meta));
    }

    [Fact]
    public async Task UnaSubidaEnCursoSobreviveAunqueSuMetaSeaAntiguo()
    {
        var (meta, part) = await CrearSubidaAsync("en-curso", TimeSpan.FromHours(50));
        Envejecer(part, TimeSpan.FromMinutes(1));

        await BarrerAsync(Servicio());

        Assert.True(File.Exists(part));
        Assert.True(File.Exists(meta));
    }

    [Fact]
    public async Task UnMetaHuerfanoSinParcialSeBorra()
    {
        var meta = Path.Combine(_uploads, "huerfano.meta");
        await File.WriteAllTextAsync(meta, JsonSerializer.Serialize(new { UploadId = "huerfano", PartPath = Path.Combine(_libreria, "no-existe.part") }));
        Envejecer(meta, TimeSpan.FromHours(30));

        await BarrerAsync(Servicio());

        Assert.False(File.Exists(meta));
    }

    [Fact]
    public async Task LosParcialesDeImportacionViejosSeBarrenYLosNuevosNo()
    {
        var viejo = Path.Combine(_dbDir, "import_aaa.part");
        var nuevo = Path.Combine(_dbDir, "import_bbb.part");
        await File.WriteAllBytesAsync(viejo, new byte[16]);
        await File.WriteAllBytesAsync(nuevo, new byte[16]);
        Envejecer(viejo, TimeSpan.FromHours(48));
        Envejecer(nuevo, TimeSpan.FromHours(1));

        await BarrerAsync(Servicio());

        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(nuevo));
    }

    [Fact]
    public async Task NoSeLlevaPorDelanteElVolcadoNiOtrosFicheros()
    {
        var volcado = Path.Combine(_dbDir, "series.sqlite");
        var indice = Path.Combine(_dbDir, "series.sqlite-wal");
        await File.WriteAllBytesAsync(volcado, new byte[32]);
        await File.WriteAllBytesAsync(indice, new byte[32]);
        Envejecer(volcado, TimeSpan.FromDays(60));
        Envejecer(indice, TimeSpan.FromDays(60));

        var libro = Path.Combine(_libreria, "tomo01.cbz");
        await File.WriteAllBytesAsync(libro, new byte[32]);
        Envejecer(libro, TimeSpan.FromDays(60));

        await BarrerAsync(Servicio());

        Assert.True(File.Exists(volcado));
        Assert.True(File.Exists(indice));
        Assert.True(File.Exists(libro));
    }

    [Fact]
    public async Task UnMetaCorruptoNoTumbaElBarrido()
    {
        var roto = Path.Combine(_uploads, "roto.meta");
        await File.WriteAllTextAsync(roto, "{ esto no es json");
        Envejecer(roto, TimeSpan.FromHours(30));

        var (meta, part) = await CrearSubidaAsync("valida", TimeSpan.FromHours(30));

        await BarrerAsync(Servicio());

        Assert.False(File.Exists(part));
        Assert.False(File.Exists(meta));
        Assert.False(File.Exists(roto));
    }
}
