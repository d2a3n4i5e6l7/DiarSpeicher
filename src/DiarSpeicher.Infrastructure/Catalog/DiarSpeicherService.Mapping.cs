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
        => CatalogDtoMapper.ToMediaDto(m, session);

    private static DiarSpeicherSeriesDto ToSeriesDto(Series s)
        => CatalogDtoMapper.ToSeriesDto(s);
}
