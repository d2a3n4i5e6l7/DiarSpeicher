using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Core.Domain.Entities;

public class Series
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Path { get; set; } = null!;
    public FileStatus Status { get; set; } = FileStatus.Ready;
    public string? ThumbnailPath { get; set; }
    public string? ThumbnailMeta { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public string? LibraryId { get; set; }
    public Library? Library { get; set; }

    public SeriesMetadata? Metadata { get; set; }
    public ICollection<Media> Media { get; set; } = new List<Media>();
    public ICollection<SeriesTag> Tags { get; set; } = new List<SeriesTag>();
}

public class SeriesMetadata
{
    public string SeriesId { get; set; } = null!;
    public Series Series { get; set; } = null!;

    public int? AgeRating { get; set; }
    public string? Characters { get; set; }
    public string? Booktype { get; set; }
    public int? Comicid { get; set; }
    public string? ComicImage { get; set; }
    public string? DescriptionFormatted { get; set; }
    public string? Genres { get; set; }
    public string? Imprint { get; set; }
    public string? Links { get; set; }
    public string? MetaType { get; set; }
    public string? PublicationRun { get; set; }
    public string? Publisher { get; set; }
    public string? Status { get; set; } // "Continuing" o "Ended"
    public string? Summary { get; set; }
    public string? Title { get; set; }
    public int? TotalIssues { get; set; }
    public int? Volume { get; set; }
    public string? Writers { get; set; }
    public int? Year { get; set; }
}
