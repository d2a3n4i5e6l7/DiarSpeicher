namespace DiarSpeicher.Core.Filesystem;

public record ExtractedPage(ContentType ContentType, byte[] Data);

public record ExtractedMetadata
{
    public string? Title { get; set; }
    public string? TitleSort { get; set; }
    public string? Series { get; set; }
    public double? Number { get; set; }
    public int? Volume { get; set; }
    public string? Summary { get; set; }
    public string? Notes { get; set; }
    public int? AgeRating { get; set; }
    public string? Genre { get; set; }
    public string? Publisher { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    public string? Writers { get; set; }
    public string? Pencillers { get; set; }
    public string? Inkers { get; set; }
    public string? Colorists { get; set; }
    public string? Letterers { get; set; }
    public string? CoverArtists { get; set; }
    public string? Editors { get; set; }
}

public record ProcessedBook
{
    public int Pages { get; init; }
    public string? Hash { get; init; }
    public string? KoreaderHash { get; init; }
    public ExtractedMetadata? Metadata { get; init; }
    public List<string> Tags { get; init; } = [];
}
