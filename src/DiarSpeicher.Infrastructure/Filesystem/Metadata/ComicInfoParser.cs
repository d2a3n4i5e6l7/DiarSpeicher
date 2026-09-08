using System.Xml.Linq;
using DiarSpeicher.Core.Filesystem;

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

    public static int? ParseAgeRating(string rating)
    {
        var clean = rating.Trim().ToLowerInvariant();

        if (int.TryParse(clean, out var numeric))
        {
            return Math.Clamp(numeric, 0, 18);
        }

        if (clean.Contains("18") || clean.Contains("adult") || clean.Contains("explicit"))
            return 18;
        if (clean.Contains("17") || clean.Contains("mature"))
            return 17;
        if (clean.Contains("15") || clean.Contains("teen plus") || clean.Contains("teen+"))
            return 15;
        if (clean.Contains("13") || clean.Contains("teen") || clean.Contains("pg-13"))
            return 13;
        if (clean.Contains("10") || clean.Contains("everyone 10+") || clean.Contains("pg"))
            return 10;
        if (clean.Contains("everyone") || clean.Contains("all ages") || clean.Contains("g"))
            return 0;

        return null;
    }
}
