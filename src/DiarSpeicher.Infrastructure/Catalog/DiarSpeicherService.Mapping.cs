namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Traduccion de entidades a los DTO que publica la API.
/// </summary>
public sealed partial class DiarSpeicherService
{
    private static DiarSpeicherLibraryDto ToLibraryDto(Library library, int mediaCount) => new()
    {
        Id = library.Id,
        Name = library.Name,
        Path = library.Path,
        Status = library.Status.ToString().ToUpperInvariant(),
        SeriesCount = library.Series?.Count ?? 0,
        MediaCount = mediaCount,
        Description = library.Description,
        Emoji = library.Emoji,
        CreatedAt = library.CreatedAt,
        UpdatedAt = library.UpdatedAt,
        LastScannedAt = library.LastScannedAt,
        Config = library.Config == null ? null : new DiarSpeicherLibraryConfigDto
        {
            LibraryType = library.Config.LibraryType.ToString(),
            LibraryPattern = library.Config.LibraryPattern.ToString(),
            DefaultReadingDir = library.Config.DefaultReadingDir.ToString(),
            DefaultReadingMode = library.Config.DefaultReadingMode.ToString(),
            DefaultLibraryViewMode = library.Config.DefaultLibraryViewMode.ToString(),
            ConvertRarToZip = library.Config.ConvertRarToZip,
            HardDeleteConversions = library.Config.HardDeleteConversions,
            GenerateFileHashes = library.Config.GenerateFileHashes,
            GenerateKoreaderHashes = library.Config.GenerateKoreaderHashes,
            ProcessMetadata = library.Config.ProcessMetadata,
            Watch = library.Config.Watch,
            HideSeriesView = library.Config.HideSeriesView,
            ThumbnailWidth = library.Config.ThumbnailWidth,
            ThumbnailHeight = library.Config.ThumbnailHeight,
            IgnoreRules = library.Config.IgnoreRules
        }
    };

    private static DiarSpeicherMediaDto ToMediaDto(Media m, ReadingSession? session)
    {
        return new DiarSpeicherMediaDto
        {
            Id = m.Id,
            Name = m.Name,
            Size = m.Size,
            Extension = m.Extension,
            Pages = m.Pages,
            Status = m.Status.ToString(),
            Hash = m.Hash,
            KoreaderHash = m.KoreaderHash,
            Path = m.Path,
            SeriesId = m.SeriesId,
            CreatedAt = m.CreatedAt,
            CurrentPage = session?.EndPage,
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

    private static DiarSpeicherSeriesDto ToSeriesDto(Series s)
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
