using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem.Processors;

namespace DiarSpeicher.Tests;

public class EpubFontTests
{
    private static EpubDeviceProfile Perfil(string familia) =>
        new() { Name = "t", Width = 1200, Height = 1920, FontSize = 40, FontFamily = familia, Theme = "core" };

    [Fact]
    public void Resolve_CargaLaFuenteElegidaDesdeDisco()
    {
        var fuentes = EpubFontProvider.Resolve(Perfil("Inter"), archive: null);

        // La imagen no trae fuentes de sistema: si esto devuelve la de Skia por defecto,
        // el selector de tipografia del perfil no esta haciendo nada.
        Assert.Equal("Inter", fuentes.Regular.FamilyName);
        Assert.True(fuentes.Bold.IsBold);
    }

    [Fact]
    public void Resolve_CadaFamiliaDaUnaFuenteDistinta()
    {
        var familias = new[] { "Inter", "Chakra Petch", "Rajdhani", "Orbitron", "JetBrains Mono" };

        var nombres = familias
            .Select(f => EpubFontProvider.Resolve(Perfil(f), archive: null).Regular.FamilyName)
            .ToList();

        Assert.Equal(familias.Length, nombres.Distinct().Count());
    }

    [Fact]
    public void Resolve_UnaFamiliaDesconocidaCaeEnElRespaldo()
    {
        var fuentes = EpubFontProvider.Resolve(Perfil("Papyrus"), archive: null);

        Assert.Equal("Inter", fuentes.Regular.FamilyName);
    }

    [Fact]
    public void Resolve_SinLibroQueAbrirNoRevienta()
    {
        var fuentes = EpubFontProvider.Resolve(Perfil(EpubFontProvider.BookEmbedded), archive: null);

        Assert.NotNull(fuentes.Regular);
        Assert.Equal("Inter", fuentes.Regular.FamilyName);
    }
}
