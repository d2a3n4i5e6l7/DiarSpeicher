namespace DiarSpeicher.Core.Domain.Entities;

/// <summary>
/// Exists because Komga clients do not show a book whose pages lack <c>width</c> and
/// <c>height</c>: they request the list, see null dimensions and never request any image.
/// Computing them means opening the archive and reading every page header, so they are stored
/// rather than recomputed on each request.
///
/// Pages are not assumed to share a size: a double-page spread is twice as wide and a cover
/// often differs, hence one row per page.
/// </summary>
public class MediaPage
{
    public string MediaId { get; set; } = null!;
    public Media Media { get; set; } = null!;

    /// <summary>1-based, as the API exposes it.</summary>
    public int Number { get; set; }

    public string FileName { get; set; } = null!;
    public string MediaType { get; set; } = "image/jpeg";

    /// <summary>Null when the image could not be decoded; the remaining pages are stored anyway.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long? SizeBytes { get; set; }
}
