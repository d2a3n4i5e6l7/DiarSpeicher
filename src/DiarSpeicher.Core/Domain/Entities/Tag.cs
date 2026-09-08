namespace DiarSpeicher.Core.Domain.Entities;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public ICollection<MediaTag> MediaTags { get; set; } = new List<MediaTag>();
    public ICollection<SeriesTag> SeriesTags { get; set; } = new List<SeriesTag>();
}

public class MediaTag
{
    public string MediaId { get; set; } = null!;
    public Media Media { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public class SeriesTag
{
    public string SeriesId { get; set; } = null!;
    public Series Series { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
