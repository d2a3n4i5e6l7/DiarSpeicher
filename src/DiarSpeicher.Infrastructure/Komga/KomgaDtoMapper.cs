using DiarSpeicher.Core.Domain.Komga;

namespace DiarSpeicher.Infrastructure.Komga;

internal static class KomgaDtoMapper
{
    internal static KomgaLibraryDto ToLibraryDto(Library l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Root = l.Path
    };

    internal static KomgaSeriesDto ToSeriesDto(Series s)
    {
        var title = s.Metadata?.Title ?? s.Name;
        var booksCount = s.Media.Count(m => m.DeletedAt == null);

        return new KomgaSeriesDto
        {
            Id = s.Id,
            LibraryId = s.LibraryId ?? string.Empty,
            Name = s.Name,
            Url = s.Path,
            Created = s.CreatedAt.ToString("O"),
            LastModified = (s.UpdatedAt ?? s.CreatedAt).ToString("O"),
            FileLastModified = (s.UpdatedAt ?? s.CreatedAt).ToString("O"),
            BooksCount = booksCount,
            BooksUnreadCount = booksCount,
            Metadata = new KomgaSeriesMetadataDto
            {
                Title = title,
                Summary = s.Metadata?.Summary ?? s.Description ?? string.Empty,
                AgeRating = s.Metadata?.AgeRating
            }
        };
    }

    private static string ResolveLastModified(Media m) =>
        (m.UpdatedAt ?? m.CreatedAt).ToString("O");

    private static KomgaMediaDto BuildBookMediaDto(ContentType ext, int pagesCount, int totalPages) =>
        new()
        {
            Status = "READY",
            MediaType = ext.ToMimeType(),
            PagesCount = pagesCount > 0 ? pagesCount : totalPages
        };

    private static KomgaBookMetadataDto BuildBookMetadataDto(Media m, List<KomgaAuthorDto> authors) =>
        new()
        {
            Title = m.Metadata?.Title ?? m.Name,
            Summary = m.Metadata?.Summary ?? string.Empty,
            Authors = authors
        };

    internal static KomgaBookDto ToBookDto(
        Media m,
        ReadingSession? s,
        int? overridePagesCount = null,
        IReadOnlyDictionary<string, int>? epubTotals = null,
        int? overrideCurrentPage = null)
    {
        var ext = ContentTypeExtensions.FromExtension(m.Extension);
        var mib = m.Size / (1024.0 * 1024.0);
        var authors = ExtractAuthors(m.Metadata?.Writers);
        var pagesCount = ResolvePagesCount(m.Id, overridePagesCount, epubTotals);
        var progress = IReadingProgress.FromSession(m, s, pagesCount);
        var readProgress = BuildReadProgress(s, overrideCurrentPage, progress.Page);
        var lastMod = ResolveLastModified(m);

        return new KomgaBookDto
        {
            Id = m.Id,
            SeriesId = m.SeriesId ?? string.Empty,
            SeriesTitle = m.Series?.Name ?? string.Empty,
            LibraryId = m.Series?.LibraryId ?? string.Empty,
            Name = m.Name,
            Url = m.Path,
            Created = m.CreatedAt.ToString("O"),
            LastModified = lastMod,
            FileLastModified = lastMod,
            SizeBytes = m.Size,
            Size = $"{mib:F1} MB",
            Media = BuildBookMediaDto(ext, pagesCount, progress.TotalPages),
            Metadata = BuildBookMetadataDto(m, authors),
            ReadProgress = readProgress
        };
    }

    private static List<KomgaAuthorDto> ExtractAuthors(string? writers)
    {
        if (string.IsNullOrWhiteSpace(writers)) return [];
        return writers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => new KomgaAuthorDto { Name = w, Role = "writer" })
            .ToList();
    }

    private static int ResolvePagesCount(string mediaId, int? overridePagesCount, IReadOnlyDictionary<string, int>? epubTotals)
    {
        if (overridePagesCount.HasValue) return overridePagesCount.Value;
        return epubTotals?.TotalFor(mediaId) ?? 0;
    }

    private static KomgaReadProgressDto? BuildReadProgress(ReadingSession? s, int? overrideCurrentPage, int? progressPage)
    {
        if (s is null) return null;
        var dateStr = (s.UpdatedAt ?? s.CreatedAt).ToString("O");
        return new KomgaReadProgressDto
        {
            Page = overrideCurrentPage ?? progressPage ?? 1,
            Completed = s.Status == ReadingStatus.Finished,
            ReadDate = dateStr,
            Created = s.CreatedAt.ToString("O"),
            LastModified = dateStr
        };
    }
}
