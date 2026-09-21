namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Traduccion de entidades del catalogo a los DTO que publica la API.
/// <para>
/// Vive fuera de <see cref="DiarSpeicherService"/> porque el escaner tambien publica estos
/// mismos DTO por el flujo de progreso: si el mapeo siguiera encerrado en el servicio, el
/// tomo que llega por SSE y el que devuelve la API acabarian divergiendo.
/// </para>
/// </summary>
public static class CatalogDtoMapper
{
    public static DiarSpeicherMediaDto ToMediaDto(Media m, ReadingSession? session)
    {
        var progress = IReadingProgress.FromSession(m, session);

        return new DiarSpeicherMediaDto
        {
            Id = m.Id,
            Name = m.Name,
            Size = m.Size,
            Extension = m.Extension,
            Pages = progress.TotalPages,
            Status = m.Status.ToString(),
            Hash = m.Hash,
            KoreaderHash = m.KoreaderHash,
            Path = m.Path,
            SeriesId = m.SeriesId,
            CreatedAt = m.CreatedAt,
            CurrentPage = progress.Page,
            IsCompleted = session?.Status == ReadingStatus.Finished,
            Metadata = m.Metadata != null ? new DiarSpeicherMediaMetadataDto
            {
                Title = m.Metadata.Title,
                Summary = m.Metadata.Summary,
                Writers = m.Metadata.Writers,
                Genre = m.Metadata.Genres,
                Publisher = m.Metadata.Publisher,
                AgeRating = m.Metadata.AgeRating,
                Number = (float?)m.Metadata.Number
            } : null
        };
    }

    public static DiarSpeicherSeriesDto ToSeriesDto(Series s)
    {
        return new DiarSpeicherSeriesDto
        {
            Id = s.Id,
            Name = s.Name,
            Path = s.Path,
            Status = s.Status.ToString(),
            LibraryId = s.LibraryId ?? string.Empty,
            MediaCount = s.Media.Count,
            Description = s.Description ?? s.Metadata?.Summary,
            CoverUpdatedAt = s.ThumbnailMeta,
            Metadata = ToSeriesMetadataDto(s.Metadata)
        };
    }

    private static DiarSpeicherSeriesMetadataDto? ToSeriesMetadataDto(SeriesMetadata? metadata)
    {
        if (metadata is null) return null;

        return new DiarSpeicherSeriesMetadataDto
        {
            Source = metadata.MetaType,
            ExternalId = metadata.Comicid,
            Title = metadata.Title,
            Summary = metadata.Summary,
            Publisher = metadata.Publisher,
            Writers = metadata.Writers,
            Genres = metadata.Genres,
            Status = metadata.Status,
            Year = metadata.Year,
            CoverUrl = metadata.ComicImage,
            Link = metadata.Links,
            FinalVolume = metadata.TotalIssues,
            Type = metadata.Booktype,
            TotalChapters = metadata.PublicationRun
        };
    }
}
