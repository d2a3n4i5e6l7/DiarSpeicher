using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Tests;

public sealed class AgeFilterMatrixTests : IDisposable
{
    private const int MaxAge = 13;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;

    public AgeFilterMatrixTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>().UseSqlite(_connection).Options;

        using var ctx = new DiarSpeicherDbContext(_options);
        ctx.Database.EnsureCreated();
        Seed(ctx);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void Seed(DiarSpeicherDbContext ctx)
    {
        var libraryId = Guid.NewGuid().ToString();
        ctx.Libraries.Add(new Library
        {
            Id = libraryId,
            Name = "Mixta",
            Path = "/libs/mixta",
            Status = FileStatus.Ready,
            Config = new LibraryConfig(),
        });

        Series Serie(string nombre, int? rating, bool conMetadata)
        {
            var s = new Series
            {
                Id = nombre,
                Name = nombre,
                Path = $"/libs/mixta/{nombre}",
                LibraryId = libraryId,
                Status = FileStatus.Ready,
            };
            if (conMetadata) s.Metadata = new SeriesMetadata { AgeRating = rating };
            ctx.Series.Add(s);
            return s;
        }

        var sinMetadata = Serie("s_sin_metadata", null, conMetadata: false);
        var ratingNulo = Serie("s_rating_nulo", null, conMetadata: true);
        var ratingApto = Serie("s_rating_apto", MaxAge - 3, conMetadata: true);
        var ratingAlto = Serie("s_rating_alto", MaxAge + 3, conMetadata: true);

        void Tomo(string id, Series? serie, int? rating, bool conMetadata)
        {
            var m = new Media
            {
                Id = id,
                Name = id,
                Path = $"/libs/mixta/{id}.cbz",
                Extension = "cbz",
                Pages = 10,
                SeriesId = serie?.Id,
                Status = FileStatus.Ready,
            };
            if (conMetadata) m.Metadata = new MediaMetadata { AgeRating = rating };
            ctx.Media.Add(m);
        }

        foreach (var (sufijo, serie) in new (string, Series?)[]
        {
            ("sin_serie", null),
            ("serie_sin_meta", sinMetadata),
            ("serie_nula", ratingNulo),
            ("serie_apta", ratingApto),
            ("serie_alta", ratingAlto),
        })
        {
            Tomo($"m_sin_meta__{sufijo}", serie, null, conMetadata: false);
            Tomo($"m_rating_nulo__{sufijo}", serie, null, conMetadata: true);
            Tomo($"m_apto__{sufijo}", serie, MaxAge - 3, conMetadata: true);
            Tomo($"m_alto__{sufijo}", serie, MaxAge + 3, conMetadata: true);
        }

        ctx.SaveChanges();
    }

    private List<string> Visibles(bool restrictOnUnset)
    {
        using var ctx = new DiarSpeicherDbContext(_options);
        var user = new AuthUser
        {
            Id = "menor",
            Username = "menor",
            IsServerOwner = false,
            AgeRestriction = MaxAge,
            RestrictOnUnset = restrictOnUnset,
        };

        return ctx.Media.ForUser(user).Select(m => m.Id).OrderBy(id => id).ToList();
    }

    [Fact]
    public void UnTomoConClasificacionPropiaAltaNuncaSeVe()
    {
        foreach (var restrictivo in new[] { true, false })
        {
            Assert.DoesNotContain(Visibles(restrictivo), id => id.StartsWith("m_alto__"));
        }
    }

    [Fact]
    public void UnTomoConClasificacionPropiaAptaSiempreSeVe()
    {
        foreach (var restrictivo in new[] { true, false })
        {
            var visibles = Visibles(restrictivo);
            Assert.Equal(5, visibles.Count(id => id.StartsWith("m_apto__")));
        }
    }

    [Fact]
    public void EnModoRestrictivoLoSinClasificarSoloSeVeSiLaSerieLoPermite()
    {
        var visibles = Visibles(restrictOnUnset: true);

        Assert.Contains("m_sin_meta__serie_apta", visibles);
        Assert.Contains("m_rating_nulo__serie_apta", visibles);

        Assert.DoesNotContain("m_sin_meta__sin_serie", visibles);
        Assert.DoesNotContain("m_sin_meta__serie_sin_meta", visibles);
        Assert.DoesNotContain("m_sin_meta__serie_nula", visibles);
        Assert.DoesNotContain("m_sin_meta__serie_alta", visibles);
    }

    [Fact]
    public void EnModoPermisivoLoSinClasificarSeVeSalvoQueLaSerieLoProhiba()
    {
        var visibles = Visibles(restrictOnUnset: false);

        Assert.Contains("m_sin_meta__sin_serie", visibles);
        Assert.Contains("m_sin_meta__serie_sin_meta", visibles);
        Assert.Contains("m_sin_meta__serie_nula", visibles);
        Assert.Contains("m_sin_meta__serie_apta", visibles);

        Assert.DoesNotContain("m_sin_meta__serie_alta", visibles);
    }

    [Fact]
    public void ElModoRestrictivoNuncaEnsenaMasQueElPermisivo()
    {
        var restrictivo = Visibles(restrictOnUnset: true);
        var permisivo = Visibles(restrictOnUnset: false);

        Assert.All(restrictivo, id => Assert.Contains(id, permisivo));
    }
}
