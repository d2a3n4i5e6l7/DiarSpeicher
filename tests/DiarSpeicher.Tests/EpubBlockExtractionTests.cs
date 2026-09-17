using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Tests;

public class EpubBlockExtractionTests
{
    // El envoltorio real de los tres EPUB que rompian la extraccion: el titulo cuelga de un
    // div o un section, y el texto del <h1> va dentro de un <a> del indice.
    private const string CapituloReal = """
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
        <head><title>Libro</title></head>
        <body>
        <div class="galley-rw">
        <section epub:type="bodymatter chapter" id="chapter002">
        <h1 class="chapter-number-1"><a href="../Text/toc.xhtml#Ref_8893a">Chapter 2 School Is Filled with Thrills!</a></h1>
        <p class="cotx">Their time at school would last a single week.</p>
        <p>The rooms at the inn were, as always, built for two.</p>
        </section>
        </div>
        </body></html>
        """;

    private static List<EpubRasterizer.RenderBlock> Extraer(string html)
    {
        var metodo = typeof(EpubRasterizer).GetMethod(
            "ExtractBlocks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (List<EpubRasterizer.RenderBlock>)metodo.Invoke(null, [html])!;
    }

    [Fact]
    public void UnTituloEnvueltoEnDivSigueSiendoUnTitulo()
    {
        var bloques = Extraer(CapituloReal);

        var titulo = bloques.FirstOrDefault(b => b.IsHeading);
        Assert.NotNull(titulo);
        Assert.Equal(1, titulo.HeadingLevel);
        Assert.Equal("Chapter 2 School Is Filled with Thrills!", titulo.Text);
    }

    [Fact]
    public void NoSePierdeNingunParrafo()
    {
        var bloques = Extraer(CapituloReal);

        Assert.Equal(3, bloques.Count);
        Assert.Contains(bloques, b => b.Text.StartsWith("Their time at school"));
        Assert.Contains(bloques, b => b.Text.StartsWith("The rooms at the inn"));
    }

    [Fact]
    public void ElTextoDeUnTituloSaleSinLasEtiquetasDeDentro()
    {
        var bloques = Extraer(CapituloReal);

        Assert.DoesNotContain(bloques, b => b.Text.Contains('<') || b.Text.Contains("href"));
    }

    [Fact]
    public void UnBloqueAnidadoNoSeCuentaDosVeces()
    {
        var bloques = Extraer("<html><body><li><p>uno</p></li><p>dos</p></body></html>");

        Assert.Equal(2, bloques.Count);
        Assert.Equal(["uno", "dos"], bloques.Select(b => b.Text));
    }

    [Fact]
    public void LosSeisNivelesDeTituloSeDistinguen()
    {
        var html = "<body>" + string.Concat(Enumerable.Range(1, 6)
            .Select(n => $"<h{n}>nivel {n}</h{n}>")) + "</body>";

        var bloques = Extraer(html);

        Assert.Equal(6, bloques.Count);
        Assert.All(bloques, b => Assert.True(b.IsHeading));
        Assert.Equal([1, 2, 3, 4, 5, 6], bloques.Select(b => b.HeadingLevel));
    }

    [Fact]
    public void UnDocumentoSinBloquesCaeAlRespaldoPorLineas()
    {
        var bloques = Extraer("<body>texto suelto sin etiquetas de bloque</body>");

        Assert.NotEmpty(bloques);
        Assert.Contains(bloques, b => b.Text.Contains("texto suelto"));
    }
}
