using System.Xml.Linq;

namespace DiarSpeicher.Infrastructure.Filesystem.Metadata;

public static class ComicInfoParser
{
    public static (ExtractedMetadata Metadata, List<string> Tags) Parse(string xmlContent)
    {
        var metadata = new ExtractedMetadata();
        var tags = new List<string>();

        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return (metadata, tags);
        }

        try
        {
            var doc = XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root is null)
            {
                return (metadata, tags);
            }

            metadata.Title = GetElementValue(root, "Title");
            metadata.TitleSort = GetElementValue(root, "TitleSort");
            metadata.Series = GetElementValue(root, "Series");
            metadata.Summary = GetElementValue(root, "Summary");
            metadata.Notes = GetElementValue(root, "Notes");
            metadata.Genre = GetElementValue(root, "Genre");
            metadata.Publisher = GetElementValue(root, "Publisher");
            metadata.FrontCoverIndex = ParseFrontCoverIndex(root);

            if (double.TryParse(GetElementValue(root, "Number"), out var num))
            {
                metadata.Number = num;
            }

            if (int.TryParse(GetElementValue(root, "Volume"), out var vol))
            {
                metadata.Volume = vol;
            }

            if (int.TryParse(GetElementValue(root, "Year"), out var year))
            {
                metadata.Year = year;
            }

            if (int.TryParse(GetElementValue(root, "Month"), out var month))
            {
                metadata.Month = month;
            }

            if (int.TryParse(GetElementValue(root, "Day"), out var day))
            {
                metadata.Day = day;
            }

            var ageRatingStr = GetElementValue(root, "AgeRating");
            if (!string.IsNullOrEmpty(ageRatingStr))
            {
                metadata.AgeRating = ParseAgeRating(ageRatingStr);
            }

            metadata.Writers = GetElementValue(root, "Writer");
            metadata.Pencillers = GetElementValue(root, "Penciller");
            metadata.Inkers = GetElementValue(root, "Inker");
            metadata.Colorists = GetElementValue(root, "Colorist");
            metadata.Letterers = GetElementValue(root, "Letterer");
            metadata.CoverArtists = GetElementValue(root, "CoverArtist");
            metadata.Editors = GetElementValue(root, "Editor");

            var tagsStr = GetElementValue(root, "Tags");
            if (!string.IsNullOrWhiteSpace(tagsStr))
            {
                tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch (Exception)
        {
            // Malformed XML returns partial or default metadata
        }

        return (metadata, tags);
    }

    private static string? GetElementValue(XElement root, string elementName)
    {
        return root.Element(elementName)?.Value?.Trim();
    }

    /// <summary>
    /// Reads the &lt;Pages&gt; block for the entry marked Type="FrontCover". The Image
    /// attribute is the zero-based position of the page inside the archive, which is what
    /// the caller indexes with once the entries are sorted.
    /// </summary>
    private static int? ParseFrontCoverIndex(XElement root)
    {
        var pages = root.Element("Pages");
        if (pages is null)
        {
            return null;
        }

        foreach (var page in pages.Elements("Page"))
        {
            var type = page.Attribute("Type")?.Value;
            if (!string.Equals(type, "FrontCover", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(page.Attribute("Image")?.Value, out var index) && index >= 0)
            {
                return index;
            }
        }

        return null;
    }

    // Los 15 valores de la enumeracion AgeRating de ComicInfo.xml. Es una lista cerrada: lo
    // que no este aqui se queda sin clasificar, que con RestrictOnUnset es lo que no se ve.
    private static readonly Dictionary<string, int> AgeRatings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Early Childhood"] = 0,
        ["Everyone"] = 0,
        ["G"] = 0,
        ["Kids to Adults"] = 0,
        ["Everyone 10+"] = 10,
        ["PG"] = 10,
        ["Teen"] = 13,
        ["MA15+"] = 15,
        ["M"] = 17,
        ["Mature 17+"] = 17,
        ["Adults Only 18+"] = 18,
        ["R18+"] = 18,
        ["X18+"] = 18,

        // Fuera de la enumeracion, pero salen en ficheros reales: MPAA y las etiquetas que
        // usan las editoriales. Van exactas igual; nunca por contenido.
        ["All Ages"] = 0,
        ["PG-13"] = 13,
        ["T"] = 13,
        ["Teen Plus"] = 15,
        ["Teen+"] = 15,
        ["T+"] = 15,
        ["Mature"] = 17,
        ["Adult"] = 18,
        ["Adults Only"] = 18,
        ["Explicit"] = 18,
        ["NC-17"] = 18,
    };

    public static int? ParseAgeRating(string rating)
    {
        var clean = rating.Trim();

        if (int.TryParse(clean, out var numeric))
        {
            return Math.Clamp(numeric, 0, 18);
        }

        return AgeRatings.TryGetValue(clean, out var edad) ? edad : null;
    }
}
