namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Lives outside <see cref="DiarSpeicherService"/> because the scanner publishes these same
/// DTOs through the progress stream: if the mapping stayed inside the service, the volume
/// arriving over SSE and the one returned by the API would drift apart.
/// </summary>
public static class CatalogDtoMapper
{
    /// <summary>
    /// An EPUB reports the progress its e-reader wrote, a percentage, and keeps the rendered-page
    /// progress of the image protocols apart in the <c>rendered*</c> fields.
    /// </summary>
    public static DiarSpeicherMediaDto ToMediaDto(Media m, ReadingSession? session)
    {
        var progress = IReadingProgress.FromSession(m, session);
        var isEpub = EpubPageMapStore.IsEpub(m);
        var hasImageProgress = isEpub && progress.Page is not null;

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
            CurrentPage = isEpub ? session?.EndPage : progress.Page,
            IsCompleted = isEpub
                ? session?.EndPercentage is not null && session.Status == ReadingStatus.Finished
                : session?.Status == ReadingStatus.Finished,
            ReadPercent = isEpub && session?.EndPercentage is decimal percentage ? (double)Math.Round(percentage * 100m, 1) : null,
            RenderedPage = hasImageProgress ? progress.Page : null,
            RenderedTotalPages = hasImageProgress ? progress.TotalPages : null,
            RenderedCompleted = hasImageProgress ? session!.IsRenderedFinished() : null,
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

    public static DiarSpeicherSeriesDto ToSeriesDto(Series s) => ToSeriesDto(s, s.Media?.Count ?? 0);

    public static DiarSpeicherSeriesDto ToSeriesDto(Series s, int mediaCount)
    {
        return new DiarSpeicherSeriesDto
        {
            Id = s.Id,
            Name = s.Name,
            Path = s.Path,
            Status = s.Status.ToString(),
            LibraryId = s.LibraryId ?? string.Empty,
            MediaCount = mediaCount,
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
