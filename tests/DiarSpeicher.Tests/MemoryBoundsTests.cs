using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.Reading;
using SkiaSharp;

namespace DiarSpeicher.Tests;

public sealed class MemoryBoundsTests : IDisposable
{
    private readonly string _tempDir;

    public MemoryBoundsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"diar_mem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ArchiveEntryRef EntradaDe(byte[] datos, string nombre, long tamanoDeclarado) =>
        new(nombre, tamanoDeclarado, _ => Task.FromResult<Stream>(new MemoryStream(datos)));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(200_000)]
    public async Task ReadAsync_DevuelveLosBytesExactos(int tamano)
    {
        var datos = new byte[tamano];
        Random.Shared.NextBytes(datos);

        var leido = await ArchiveEntryReader.ReadAsync(EntradaDe(datos, "01.jpg", tamano), default);

        Assert.Equal(datos, leido.Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task ReadAsync_UnTamanoDeclaradoAbsurdoNoRompeLaLectura(long declarado)
    {
        var datos = new byte[5000];
        Random.Shared.NextBytes(datos);

        var leido = await ArchiveEntryReader.ReadAsync(EntradaDe(datos, "01.jpg", declarado), default);

        Assert.Equal(datos, leido.Data);
    }

    [Fact]
    public void LosCerrojosDelMapaDePaginasSonUnNumeroFijo()
    {
        var campo = typeof(EpubPageMapStore)
            .GetField("Builders", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(typeof(SemaphoreSlim[]), campo.FieldType);

        var cerrojos = (SemaphoreSlim[])campo.GetValue(null)!;
        Assert.Equal(64, cerrojos.Length);
        Assert.All(cerrojos, c => Assert.Equal(1, c.CurrentCount));
    }

    [Fact]
    public void LaMismaClaveSiempreDaElMismoCerrojo()
    {
        var metodo = typeof(EpubPageMapStore)
            .GetMethod("BuilderFor", BindingFlags.NonPublic | BindingFlags.Static)!;

        var claves = Enumerable.Range(0, 500).Select(i => $"libro{i}:1200x1920:40").ToList();

        foreach (var clave in claves)
        {
            Assert.Same(metodo.Invoke(null, [clave]), metodo.Invoke(null, [clave]));
        }

        var distintos = claves.Select(c => metodo.Invoke(null, [c])).Distinct().Count();
        Assert.True(distintos > 1, "todas las claves cayeron en el mismo cerrojo");
        Assert.True(distintos <= 64, $"aparecieron {distintos} cerrojos distintos");
    }

    private static byte[] Ofuscar(byte[] fuente, string uid)
    {
        var limpio = new string(uid.Where(c => c is not (' ' or '\t' or '\r' or '\n')).ToArray());
        var clave = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(limpio));

        var fuera = (byte[])fuente.Clone();
        for (var i = 0; i < Math.Min(1040, fuera.Length); i++) fuera[i] ^= clave[i % clave.Length];
        return fuera;
    }

    private string CrearEpubConFuente(string uid, byte[] fuente, bool ofuscada = false)
    {
        var ruta = Path.Combine(_tempDir, $"libro_{Guid.NewGuid():N}.epub");
        using var zip = ZipFile.Open(ruta, ZipArchiveMode.Create);

        void Escribir(string nombre, string contenido)
        {
            using var w = new StreamWriter(zip.CreateEntry(nombre).Open());
            w.Write(contenido);
        }

        Escribir("META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        Escribir("OEBPS/content.opf", $"""
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="bookid" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">{uid}</dc:identifier>
              </metadata>
            </package>
            """);

        Escribir("OEBPS/style.css", "@font-face { font-family: X; font-weight: 400; src: url(fonts/regular.ttf); }");

        using (var s = zip.CreateEntry("OEBPS/fonts/regular.ttf").Open())
        {
            s.Write(ofuscada ? Ofuscar(fuente, uid) : fuente);
        }

        if (ofuscada)
        {
            Escribir("META-INF/encryption.xml", """
                <?xml version="1.0"?>
                <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <enc:EncryptedData xmlns:enc="http://www.w3.org/2001/04/xmlenc#">
                    <enc:EncryptionMethod Algorithm="http://www.idpf.org/2008/embedding"/>
                    <enc:CipherData><enc:CipherReference URI="OEBPS/fonts/regular.ttf"/></enc:CipherData>
                  </enc:EncryptedData>
                </encryption>
                """);
        }

        return ruta;
    }

    private static EpubDeviceProfile PerfilDelLibro() => new()
    {
        Name = "t",
        Width = 1200,
        Height = 1920,
        FontSize = 40,
        FontFamily = EpubFontProvider.BookEmbedded,
        Theme = "core"
    };

    private static int FuentesEnCache()
    {
        var campo = typeof(EpubFontProvider)
            .GetField("BookFaces", BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((System.Collections.ICollection)campo.GetValue(null)!).Count;
    }

    private SKTypeface? ResolverDesde(string uid, byte[] fuente, bool ofuscada = false)
    {
        var ruta = CrearEpubConFuente(uid, fuente, ofuscada);
        using var zip = ZipFile.OpenRead(ruta);
        return EpubFontProvider.Resolve(PerfilDelLibro(), zip).Regular;
    }

    [Fact]
    public void LaCacheDeFuentesDelLibroNoCreceSinTope()
    {
        for (var i = 0; i < 40; i++)
        {
            var distinta = new byte[3000];
            Random.Shared.NextBytes(distinta);
            ResolverDesde($"urn:uuid:libro-{i}", distinta);
        }

        Assert.InRange(FuentesEnCache(), 1, 16);
    }

    [Fact]
    public void LaMismaFuenteEnVolumenesDistintosSeCacheaUnaSolaVez()
    {
        var fuente = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "rajdhani-500.ttf"));

        var volumen1 = ResolverDesde("urn:uuid:serie-vol-1", fuente);
        var trasElPrimero = FuentesEnCache();
        var volumen2 = ResolverDesde("urn:uuid:serie-vol-2", fuente);

        Assert.StartsWith("Rajdhani", volumen1!.FamilyName);
        Assert.Same(volumen1, volumen2);
        Assert.Equal(trasElPrimero, FuentesEnCache());
    }

    [Fact]
    public void UnaFuenteOfuscadaDaElMismoHashQueLaLimpia()
    {
        var fuente = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "chakra-petch-400.ttf"));

        var limpia = ResolverDesde("urn:uuid:sin-mascara", fuente);
        var entradas = FuentesEnCache();
        var enmascarada = ResolverDesde("urn:uuid:con-mascara", fuente, ofuscada: true);

        Assert.Equal("Chakra Petch", enmascarada!.FamilyName);
        Assert.Same(limpia, enmascarada);
        Assert.Equal(entradas, FuentesEnCache());
    }

    [Fact]
    public void UnEpubConFuenteIncrustadaLaUsaDeVerdad()
    {
        var fuente = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "orbitron-500.ttf"));
        var ruta = CrearEpubConFuente("urn:uuid:solo-uno", fuente);

        using var zip = ZipFile.OpenRead(ruta);
        var fuentes = EpubFontProvider.Resolve(new EpubDeviceProfile
        {
            Name = "t",
            Width = 1200,
            Height = 1920,
            FontSize = 40,
            FontFamily = EpubFontProvider.BookEmbedded,
            Theme = "core"
        }, zip);

        Assert.Equal("Orbitron", fuentes.Regular.FamilyName);
    }

    [Fact]
    public void LosStreamsDelOpfSeCierranYElUidSeLee()
    {
        var fuente = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "inter-400.ttf"));
        var ruta = CrearEpubConFuente("urn:uuid:abc-123", fuente);

        using var zip = ZipFile.OpenRead(ruta);

        var metodo = typeof(EpubFontProvider)
            .GetMethod("UniqueIdentifier", BindingFlags.NonPublic | BindingFlags.Static)!;

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal("urn:uuid:abc-123", (string)metodo.Invoke(null, [zip])!);
        }
    }
}
