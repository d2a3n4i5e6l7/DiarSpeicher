using DiarSpeicher.Infrastructure.Filesystem.Metadata;

namespace DiarSpeicher.Tests;

public class AgeRatingTests
{
    [Theory]
    [InlineData("Adults Only 18+", 18)]
    [InlineData("Explicit", 18)]
    [InlineData("Mature 17+", 17)]
    [InlineData("Teen Plus", 15)]
    [InlineData("Teen", 13)]
    [InlineData("PG-13", 13)]
    [InlineData("Everyone 10+", 10)]
    [InlineData("PG", 10)]
    [InlineData("Everyone", 0)]
    [InlineData("All Ages", 0)]
    [InlineData("G", 0)]
    public void LasClasificacionesConocidasSeLeenBien(string texto, int esperado) =>
        Assert.Equal(esperado, ComicInfoParser.ParseAgeRating(texto));

    [Theory]
    [InlineData("Gore")]
    [InlineData("Graphic Violence")]
    [InlineData("Grotesque")]
    [InlineData("Gang Violence")]
    public void UnaPalabraConGNoSeClasificaComoAptaParaTodos(string texto)
    {
        var edad = ComicInfoParser.ParseAgeRating(texto);

        Assert.True(
            edad is null or > 0,
            $"'{texto}' se clasifico como {edad}: apta para todos los publicos.");
    }

    [Theory]
    [InlineData("Upgrade")]
    [InlineData("Espionage")]
    public void UnaPalabraConPgNoSeClasificaComoDiez(string texto)
    {
        var edad = ComicInfoParser.ParseAgeRating(texto);

        Assert.True(edad is null or > 10, $"'{texto}' se clasifico como {edad}.");
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Unrated", null)]
    [InlineData("Desconocido", null)]
    public void LoQueNoSeReconoceSeQuedaSinClasificar(string texto, int? esperado) =>
        Assert.Equal(esperado, ComicInfoParser.ParseAgeRating(texto));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    [InlineData("18", 18)]
    [InlineData("99", 18)]
    [InlineData("-5", 0)]
    public void UnNumeroSueltoSeAcotaEntreCeroYDieciocho(string texto, int esperado) =>
        Assert.Equal(esperado, ComicInfoParser.ParseAgeRating(texto));
}
