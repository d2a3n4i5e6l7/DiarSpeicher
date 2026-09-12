using DiarSpeicher.Core.Domain.MangaBaka;
using DiarSpeicher.Infrastructure.Metadata;

namespace DiarSpeicher.Api.GraphQL;

/// <summary>
/// El catalogo externo entra en el esquema que ya existe, junto a Series, Media y Library.
/// El frontend no aprende un protocolo nuevo para esto ni habla con mangabaka.org: pregunta
/// al mismo sitio que para todo lo demas.
/// </summary>
[ExtendObjectType<Query>]
public class MangaBakaQueries
{
    protected MangaBakaQueries() { }

    /// <summary>Candidatos del volcado para un titulo. Lista vacia si no hay volcado.</summary>
    public static async Task<IReadOnlyList<MangaBakaCandidateDto>> GetMangaBakaCandidates(
        string query,
        IMangaBakaCatalog catalog,
        CancellationToken ct,
        int limit = 10) =>
        await catalog.SearchAsync(query, limit, ct);

    /// <summary>Ficha completa de una serie del volcado.</summary>
    public static async Task<MangaBakaSeriesDto?> GetMangaBakaSeries(
        int id,
        IMangaBakaCatalog catalog,
        CancellationToken ct) =>
        await catalog.GetAsync(id, ct);

    /// <summary>Candidatos para una serie ya indexada, buscando por su nombre de carpeta.</summary>
    public static async Task<IReadOnlyList<MangaBakaCandidateDto>> GetMangaBakaMatchesForSeries(
        string seriesId,
        ISeriesMetadataMatcher matcher,
        CancellationToken ct,
        int limit = 10) =>
        await matcher.SuggestAsync(seriesId, limit, ct);
}
