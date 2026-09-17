using System.Reflection;
using System.Text;
using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.WebUtilities;

namespace DiarSpeicher.Tests;

public sealed class MultipartSubpathTests
{
    private const string Boundary = "----diarboundary";

    private static MultipartReader ReaderFor(params (string Disposition, string Body)[] sections)
    {
        var sb = new StringBuilder();
        foreach (var (disposition, body) in sections)
        {
            sb.Append("--").Append(Boundary).Append("\r\n");
            sb.Append("Content-Disposition: ").Append(disposition).Append("\r\n\r\n");
            sb.Append(body).Append("\r\n");
        }
        sb.Append("--").Append(Boundary).Append("--\r\n");

        return new MultipartReader(Boundary, new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static async Task<(string? Subpath, DiarSpeicherUploadFileInput? FirstFile)> ReadUntilFirstFileAsync(MultipartReader reader)
    {
        var metodo = typeof(DiarSpeicherEndpoints)
            .GetMethod("ReadUntilFirstFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<(string?, DiarSpeicherUploadFileInput?)>)metodo.Invoke(null, [reader, CancellationToken.None])!;
    }

    private static IAsyncEnumerable<DiarSpeicherUploadFileInput> ReadMultipartFilesAsync(
        MultipartReader reader,
        DiarSpeicherUploadFileInput? firstFile)
    {
        var metodo = typeof(DiarSpeicherEndpoints)
            .GetMethod("ReadMultipartFilesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IAsyncEnumerable<DiarSpeicherUploadFileInput>)metodo.Invoke(null, [reader, firstFile, CancellationToken.None])!;
    }

    [Fact]
    public async Task ElSubpathDelFormularioLlegaAntesDeResolverElDestino()
    {
        var reader = ReaderFor(
            ("form-data; name=\"subpath\"", "Shonen/Bleach"),
            ("form-data; name=\"files\"; filename=\"vol01.cbz\"", "PK-falso"));

        var (subpath, firstFile) = await ReadUntilFirstFileAsync(reader);

        Assert.Equal("Shonen/Bleach", subpath);
        Assert.NotNull(firstFile);
        Assert.Equal("vol01.cbz", firstFile.FileName);
    }

    [Fact]
    public async Task ElPrimerFicheroNoSePierdeAlAdelantarLaLectura()
    {
        var reader = ReaderFor(
            ("form-data; name=\"subpath\"", "Seinen"),
            ("form-data; name=\"files\"; filename=\"vol01.cbz\"", "uno"),
            ("form-data; name=\"files\"; filename=\"vol02.cbz\"", "dos"),
            ("form-data; name=\"files\"; filename=\"vol03.cbz\"", "tres"));

        var (_, firstFile) = await ReadUntilFirstFileAsync(reader);

        var nombres = new List<string>();
        await foreach (var f in ReadMultipartFilesAsync(reader, firstFile))
        {
            nombres.Add(f.FileName);
        }

        Assert.Equal(["vol01.cbz", "vol02.cbz", "vol03.cbz"], nombres);
    }

    [Fact]
    public async Task SinSubpathElResultadoEsNuloYLosFicherosSiguenLlegando()
    {
        var reader = ReaderFor(("form-data; name=\"files\"; filename=\"suelto.cbz\"", "x"));

        var (subpath, firstFile) = await ReadUntilFirstFileAsync(reader);

        Assert.Null(subpath);
        Assert.NotNull(firstFile);

        var nombres = new List<string>();
        await foreach (var f in ReadMultipartFilesAsync(reader, firstFile))
        {
            nombres.Add(f.FileName);
        }

        Assert.Equal(["suelto.cbz"], nombres);
    }

    [Fact]
    public async Task UnaSubidaSinNingunFicheroNoRevienta()
    {
        var reader = ReaderFor(("form-data; name=\"subpath\"", "Solo/Carpeta"));

        var (subpath, firstFile) = await ReadUntilFirstFileAsync(reader);

        Assert.Equal("Solo/Carpeta", subpath);
        Assert.Null(firstFile);

        var nombres = new List<string>();
        await foreach (var f in ReadMultipartFilesAsync(reader, firstFile))
        {
            nombres.Add(f.FileName);
        }

        Assert.Empty(nombres);
    }

    [Fact]
    public async Task UnCampoSinNombreDeFicheroSeIgnora()
    {
        var reader = ReaderFor(
            ("form-data; name=\"otro\"", "ruido"),
            ("form-data; name=\"files\"; filename=\"\"", ""),
            ("form-data; name=\"files\"; filename=\"bueno.cbz\"", "y"));

        var (_, firstFile) = await ReadUntilFirstFileAsync(reader);

        Assert.NotNull(firstFile);
        Assert.Equal("bueno.cbz", firstFile.FileName);
    }
}
