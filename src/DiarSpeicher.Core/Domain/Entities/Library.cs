using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Core.Domain.Entities;

public class Library
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Path { get; set; } = null!;
    public FileStatus Status { get; set; } = FileStatus.Ready;
    public string? Emoji { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? ThumbnailMeta { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }

    public int ConfigId { get; set; }
    public LibraryConfig Config { get; set; } = null!;

    public ICollection<Series> Series { get; set; } = new List<Series>();
    public ICollection<LibraryExclusion> ExcludedUsers { get; set; } = new List<LibraryExclusion>();
}

public class LibraryConfig
{
    public int Id { get; set; }
    public string? LibraryId { get; set; }
    public Library? Library { get; set; }

    public bool ConvertRarToZip { get; set; }
    public bool HardDeleteConversions { get; set; }
    public ReadingDirection DefaultReadingDir { get; set; } = ReadingDirection.LeftToRight;
    public ReadingMode DefaultReadingMode { get; set; } = ReadingMode.Paged;
    public bool GenerateFileHashes { get; set; } = true;
    public bool GenerateKoreaderHashes { get; set; } = true;
    public bool ProcessMetadata { get; set; } = true;
    public bool Watch { get; set; } = false;
    public LibraryPattern LibraryPattern { get; set; } = LibraryPattern.SeriesBased;
    public LibraryViewMode DefaultLibraryViewMode { get; set; } = LibraryViewMode.Grid;
    public bool HideSeriesView { get; set; } = false;
    public LibraryType LibraryType { get; set; } = LibraryType.Mixed;
    public int ThumbnailWidth { get; set; } = 400;
    public int ThumbnailHeight { get; set; } = 600;
    public string? IgnoreRules { get; set; }
}

public class LibraryExclusion
{
    public int Id { get; set; }
    public string LibraryId { get; set; } = null!;
    public Library Library { get; set; } = null!;

    public string UserId { get; set; } = null!;
}
