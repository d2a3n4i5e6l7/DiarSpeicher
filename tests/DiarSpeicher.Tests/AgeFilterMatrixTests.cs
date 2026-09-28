using Dapper;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data.Extensions;
using Xunit;

namespace DiarSpeicher.Tests;

public sealed class AgeFilterMatrixTests : IDisposable
{
    private const int MaxAge = 13;
    private readonly TestSqliteDatabase _db;

    public AgeFilterMatrixTests()
    {
        _db = new TestSqliteDatabase();
        Seed();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void Seed()
    {
        var conn = _db.Connection;
        var libraryId = Guid.NewGuid().ToString();

        const string insertConfigSql = """
            INSERT INTO "LibraryConfigs" ("Id", "DefaultReadingDir", "DefaultReadingMode", "LibraryPattern", "LibraryType")
            VALUES (1, 'LTR', 'Webtoon', 'Default', 'Manga');
            """;
        conn.Execute(insertConfigSql);

        const string insertLibrarySql = """
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, @Name, @Path, 'Ready', 1, @CreatedAt);
            """;
        conn.Execute(insertLibrarySql, new { Id = libraryId, Name = "Mixta", Path = "/libs/mixta", CreatedAt = DateTimeOffset.UtcNow });

        void InsertSerie(string nombre, int? rating, bool conMetadata)
        {
            const string insertSeriesSql = """
                INSERT INTO "Series" ("Id", "Name", "Path", "LibraryId", "Status", "CreatedAt")
                VALUES (@Id, @Name, @Path, @LibraryId, 'Ready', @CreatedAt);
                """;
            conn.Execute(insertSeriesSql, new
            {
                Id = nombre,
                Name = nombre,
                Path = $"/libs/mixta/{nombre}",
                LibraryId = libraryId,
                CreatedAt = DateTimeOffset.UtcNow
            });

            if (conMetadata)
            {
                const string insertMetaSql = """
                    INSERT INTO "SeriesMetadata" ("SeriesId", "AgeRating")
                    VALUES (@SeriesId, @AgeRating);
                    """;
                conn.Execute(insertMetaSql, new { SeriesId = nombre, AgeRating = rating });
            }
        }

        InsertSerie("serie_sin_meta", null, conMetadata: false);
        InsertSerie("serie_nula", null, conMetadata: true);
        InsertSerie("serie_apta", MaxAge - 3, conMetadata: true);
        InsertSerie("serie_alta", MaxAge + 3, conMetadata: true);

        void InsertTomo(string id, string? seriesId, int? rating, bool conMetadata)
        {
            const string insertMediaSql = """
                INSERT INTO "Media" ("Id", "Name", "Path", "Extension", "Size", "SeriesId", "Status", "CreatedAt")
                VALUES (@Id, @Name, @Path, 'cbz', 1, @SeriesId, 'Ready', @CreatedAt);
                """;
            conn.Execute(insertMediaSql, new
            {
                Id = id,
                Name = id,
                Path = $"/libs/mixta/{id}.cbz",
                SeriesId = seriesId,
                CreatedAt = DateTimeOffset.UtcNow
            });

            if (conMetadata)
            {
                const string insertMetaSql = """
                    INSERT INTO "MediaMetadata" ("MediaId", "AgeRating")
                    VALUES (@MediaId, @AgeRating);
                    """;
                conn.Execute(insertMetaSql, new { MediaId = id, AgeRating = rating });
            }
        }

        foreach (var (sufijo, serieId) in new (string, string?)[]
        {
            ("sin_serie", null),
            ("serie_sin_meta", "serie_sin_meta"),
            ("serie_nula", "serie_nula"),
            ("serie_apta", "serie_apta"),
            ("serie_alta", "serie_alta"),
        })
        {
            InsertTomo($"m_sin_meta__{sufijo}", serieId, null, conMetadata: false);
            InsertTomo($"m_rating_nulo__{sufijo}", serieId, null, conMetadata: true);
            InsertTomo($"m_apto__{sufijo}", serieId, MaxAge - 3, conMetadata: true);
            InsertTomo($"m_alto__{sufijo}", serieId, MaxAge + 3, conMetadata: true);
        }
    }

    private List<string> Visibles(bool restrictOnUnset)
    {
        var user = new AuthUser
        {
            Id = "menor",
            Username = "menor",
            IsServerOwner = false,
            AgeRestriction = MaxAge,
            RestrictOnUnset = restrictOnUnset,
        };

        var (where, parameters) = CatalogSqlFilters.BuildMediaVisibilityFilter(user, "m", "mm", "s", "sm");
        var sql = $"""
            SELECT m."Id"
            FROM "Media" m
            LEFT JOIN "MediaMetadata" mm ON m."Id" = mm."MediaId"
            LEFT JOIN "Series" s ON m."SeriesId" = s."Id"
            LEFT JOIN "SeriesMetadata" sm ON s."Id" = sm."SeriesId"
            WHERE {where}
            ORDER BY m."Id" ASC;
            """;

        return _db.Connection.Query<string>(sql, parameters).ToList();
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
