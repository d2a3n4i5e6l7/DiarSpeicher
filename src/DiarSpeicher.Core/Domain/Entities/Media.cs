using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Core.Domain.Entities;

public class Media
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string Name { get; set; } = null!;
    public long Size { get; set; }
    public string Extension { get; set; } = null!;
    public int Pages { get; set; }
    public string Path { get; set; } = null!;
    public FileStatus Status { get; set; } = FileStatus.Ready;

    public string? Hash { get; set; }
    public string? KoreaderHash { get; set; }

    public string? ThumbnailPath { get; set; }
    public string? ThumbnailMeta { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public string? SeriesId { get; set; }
    public Series? Series { get; set; }

    public MediaMetadata? Metadata { get; set; }
    public ICollection<MediaTag> Tags { get; set; } = new List<MediaTag>();
    public ICollection<ReadingSession> ReadingSessions { get; set; } = new List<ReadingSession>();
}

public class MediaMetadata
{
    public int Id { get; set; }
    public string? MediaId { get; set; }
    public Media? Media { get; set; }

    public int? AgeRating { get; set; }
    public string? Characters { get; set; }
    public string? Colorists { get; set; }
    public string? CoverArtists { get; set; }
    public string? Format { get; set; }
    public int? Day { get; set; }
    public string? Editors { get; set; }
    public string? Genres { get; set; }
    public string? IdentifierAmazon { get; set; }
    public string? IdentifierCalibre { get; set; }
    public string? IdentifierGoogle { get; set; }
    public string? IdentifierIsbn { get; set; }
    public string? IdentifierMobiAsin { get; set; }
    public string? IdentifierUuid { get; set; }
    public string? Inkers { get; set; }
    public string? Language { get; set; }
    public string? Letterers { get; set; }
    public string? Links { get; set; }
    public int? Month { get; set; }
    public string? Notes { get; set; }
    public decimal? Number { get; set; }
    public int? PageCount { get; set; }
    public string? Pencillers { get; set; }
    public string? Publisher { get; set; }
    public string? Series { get; set; }
    public string? SeriesGroup { get; set; }
    public string? StoryArc { get; set; }
    public decimal? StoryArcNumber { get; set; }
    public string? Summary { get; set; }
    public string? Teams { get; set; }
    public string? Title { get; set; }
    public string? TitleSort { get; set; }
    public int? Volume { get; set; }
    public string? Writers { get; set; }
    public int? Year { get; set; }
}
