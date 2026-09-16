namespace DiarSpeicher.Core.Domain.Entities;

public class EpubPageMap
{
    public string MediaId { get; set; } = null!;
    public Media Media { get; set; } = null!;
    public string ProfileKey { get; set; } = null!;
    public int TotalPages { get; set; }
    public DateTimeOffset FileModifiedAt { get; set; }
    public DateTimeOffset BuiltAt { get; set; } = DateTimeOffset.UtcNow;
}
