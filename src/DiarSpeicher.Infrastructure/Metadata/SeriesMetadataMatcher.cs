using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.MangaBaka;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Infrastructure.Metadata;

public interface ISeriesMetadataMatcher
{
    Task<IReadOnlyList<MangaBakaCandidateDto>> SuggestAsync(string seriesId, int limit, CancellationToken cancellationToken = default);
    Task<bool> ApplyAsync(string seriesId, int mangaBakaId, CancellationToken cancellationToken = default);
    Task<bool> ClearAsync(string seriesId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Empareja una serie local con una del volcado y vuelca sus datos en
/// <see cref="SeriesMetadata"/>.
/// <para>
/// El emparejado no se hace solo: <c>Galaxy Angel</c> devuelve cinco obras distintas en el
/// volcado, y elegir por parecido de título acabaría escribiendo la ficha equivocada. Este
/// servicio propone; decide una persona.
/// </para>
/// <para>
/// Lo externo vive en <c>SeriesMetadata</c> y lo que venía dentro del archivo, en
/// <c>MediaMetadata</c>. Al no compartir tabla, revertir es borrar una fila y lo embebido
/// sigue intacto: una metadata externa equivocada no destruye la del fichero.
/// </para>
/// </summary>
public class SeriesMetadataMatcher : ISeriesMetadataMatcher
{
    /// <summary>Marca de procedencia en <c>SeriesMetadata.MetaType</c>.</summary>
    public const string SourceTag = "mangabaka";

    private readonly DiarSpeicherDbContext _dbContext;
    private readonly IMangaBakaCatalog _catalog;

    public SeriesMetadataMatcher(DiarSpeicherDbContext dbContext, IMangaBakaCatalog catalog)
    {
        _dbContext = dbContext;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<MangaBakaCandidateDto>> SuggestAsync(
        string seriesId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var name = await _dbContext.Series
            .Where(s => s.Id == seriesId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(name)) return [];

        return await _catalog.SearchAsync(CleanFolderName(name), limit, cancellationToken);
    }

    public async Task<bool> ApplyAsync(string seriesId, int mangaBakaId, CancellationToken cancellationToken = default)
    {
        var series = await _dbContext.Series
            .Include(s => s.Metadata)
            .FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken);

        if (series is null) return false;

        var external = await _catalog.GetAsync(mangaBakaId, cancellationToken);
        if (external is null) return false;

        var metadata = series.Metadata;
        if (metadata is null)
        {
            metadata = new SeriesMetadata { SeriesId = series.Id };
            _dbContext.SeriesMetadata.Add(metadata);
            series.Metadata = metadata;
        }

        metadata.MetaType = SourceTag;
        metadata.Comicid = external.Id;
        metadata.Title = external.Title;
        metadata.Summary = external.Description;
        metadata.Publisher = external.Publishers;
        metadata.Writers = external.Authors;
        metadata.Genres = external.Genres;
        metadata.Status = external.Status;
        metadata.Year = external.Year;
        metadata.ComicImage = external.CoverUrl;
        metadata.Links = external.CanonicalUrl;
        metadata.Booktype = external.Type;
        metadata.TotalIssues = ParseCount(external.FinalVolume);
        metadata.PublicationRun = external.TotalChapters;

        series.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ClearAsync(string seriesId, CancellationToken cancellationToken = default)
    {
        var metadata = await _dbContext.SeriesMetadata
            .FirstOrDefaultAsync(m => m.SeriesId == seriesId, cancellationToken);

        if (metadata is null) return false;

        _dbContext.SeriesMetadata.Remove(metadata);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Quita del nombre de carpeta lo que no es el título: corchetes de scanlation, rangos
    /// de tomos, años y etiquetas de calidad. Es la diferencia entre buscar
    /// "Accel World Tomos [01-08][Completo]" y buscar "Accel World".
    /// </summary>
    internal static string CleanFolderName(string name)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(name, @"[\[\(\{][^\]\)\}]*[\]\)\}]", " ");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\b(tomos?|vol(umen|ume)?s?|caps?|cap[ií]tulos?)\b", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[_\.]+", " ");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s{2,}", " ");
        return cleaned.Trim();
    }

    /// <summary>
    /// <c>final_volume</c> llega como texto y a veces trae adornos ("12", "12+", "?").
    /// Solo interesa el número: es lo que permite decir "3 de 12".
    /// </summary>
    private static int? ParseCount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }
}
