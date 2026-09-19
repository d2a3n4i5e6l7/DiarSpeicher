using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Tests;

public class ContentTypeRangeTests
{
    private static readonly ContentType[] Imagenes =
    [
        ContentType.Avif, ContentType.Heif, ContentType.Png, ContentType.Jpeg,
        ContentType.JpegXl, ContentType.Webp, ContentType.Gif
    ];

    private static readonly ContentType[] Servibles =
    [
        ContentType.Pdf, ContentType.EpubZip, ContentType.Zip,
        ContentType.ComicZip, ContentType.Rar, ContentType.ComicRar
    ];

    private static void AssertContiguos(ContentType[] grupo, string nombre)
    {
        var valores = grupo.Select(t => (int)t).Order().ToArray();

        Assert.True(
            valores.Length == valores[^1] - valores[0] + 1,
            $"Los valores de {nombre} dejaron de ser contiguos en el enum ({string.Join(", ", valores)}). " +
            "IsImage e IsSupportedMedia comparan por rango: mueve el tipo nuevo dentro del bloque " +
            "o cambia esos metodos a una comprobacion de pertenencia.");
    }

    [Fact]
    public void LosTiposDeImagenSiguenSiendoContiguos() => AssertContiguos(Imagenes, "imagen");

    [Fact]
    public void LosTiposServiblesSiguenSiendoContiguos() => AssertContiguos(Servibles, "media servible");

    [Fact]
    public void IsImageClasificaTodosLosMiembrosDelEnum()
    {
        foreach (var tipo in Enum.GetValues<ContentType>())
        {
            Assert.Equal(Imagenes.Contains(tipo), tipo.IsImage());
        }
    }

    [Fact]
    public void IsSupportedMediaClasificaTodosLosMiembrosDelEnum()
    {
        foreach (var tipo in Enum.GetValues<ContentType>())
        {
            Assert.Equal(Servibles.Contains(tipo), tipo.IsSupportedMedia());
        }
    }

    [Theory]
    [InlineData(ContentType.Unknown)]
    [InlineData(ContentType.Txt)]
    [InlineData(ContentType.Xml)]
    [InlineData(ContentType.XHtml)]
    [InlineData(ContentType.Html)]
    [InlineData(ContentType.Pdf)]
    public void LoQueNoEsImagenNoSeCuelaComoImagen(ContentType tipo) => Assert.False(tipo.IsImage());

    [Theory]
    [InlineData(ContentType.Unknown)]
    [InlineData(ContentType.Txt)]
    [InlineData(ContentType.Png)]
    [InlineData(ContentType.Jpeg)]
    [InlineData(ContentType.Html)]
    public void UnaPaginaSueltaNoEsUnLibroServible(ContentType tipo) => Assert.False(tipo.IsSupportedMedia());

    [Fact]
    public void ElRangoNoSeComeAlVecino()
    {
        Assert.False(((ContentType)((int)ContentType.Avif - 1)).IsImage());
        Assert.False(((ContentType)((int)ContentType.Gif + 1)).IsImage());
        Assert.False(((ContentType)((int)ContentType.Pdf - 1)).IsSupportedMedia());
        Assert.False(((ContentType)((int)ContentType.ComicRar + 1)).IsSupportedMedia());
    }
}
