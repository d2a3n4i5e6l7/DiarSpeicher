using System.IO.Compression;
using System.Xml.Linq;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

internal static class EpubOpfReader
{
    public static async Task<List<ZipArchiveEntry>> GetSpineEntriesAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var opfPath = await FindOpfPathAsync(archive, cancellationToken);
        if (string.IsNullOrEmpty(opfPath)) return [];

        var opfEntry = archive.GetEntry(opfPath);
        if (opfEntry == null) return [];

        try
        {
            var doc = await LoadXmlAsync(opfEntry, cancellationToken);
            var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? "";
            return ResolveSpineItems(archive, doc, opfDir);
        }
        catch
        {
            return [];
        }
    }

    private static List<ZipArchiveEntry> ResolveSpineItems(ZipArchive archive, XDocument doc, string opfDir)
    {
        var manifest = doc.Descendants()
            .Where(e => e.Name.LocalName == "item")
            .Select(e => new { Id = e.Attribute("id")?.Value, Href = e.Attribute("href")?.Value })
            .Where(x => !string.IsNullOrEmpty(x.Id) && !string.IsNullOrEmpty(x.Href))
            .ToDictionary(x => x.Id!, x => x.Href!);

        var spine = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "spine");
        if (spine == null) return [];

        var entries = new List<ZipArchiveEntry>();
        foreach (var itemref in spine.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            var entry = FindSpineEntry(archive, itemref, manifest, opfDir);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static ZipArchiveEntry? FindSpineEntry(
        ZipArchive archive,
        XElement itemref,
        Dictionary<string, string> manifest,
        string opfDir)
    {
        var idref = itemref.Attribute("idref")?.Value;
        if (string.IsNullOrEmpty(idref) || !manifest.TryGetValue(idref, out var href)) return null;

        var fullHref = string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}";
        return archive.GetEntry(fullHref) ??
            archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), fullHref.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> ReadOpfDataAsync(
        ZipArchiveEntry opfEntry, ExtractedMetadata metadata, List<string> tags, CancellationToken cancellationToken)
    {
        var doc = await LoadXmlAsync(opfEntry, cancellationToken);
        PopulateOpfMetadata(doc, metadata, tags);

        var spineElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "spine");
        var chapterCount = spineElem?.Descendants().Count(e => e.Name.LocalName == "itemref") ?? 0;
        return chapterCount == 0
            ? doc.Descendants().Count(e => e.Name.LocalName == "itemref")
            : chapterCount;
    }

    private static void PopulateOpfMetadata(XDocument doc, ExtractedMetadata metadata, List<string> tags)
    {
        var titleElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "title");
        if (titleElem is not null) metadata.Title = titleElem.Value.Trim();

        var creatorElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "creator");
        if (creatorElem is not null) metadata.Writers = creatorElem.Value.Trim();

        var descElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "description");
        if (descElem is not null) metadata.Summary = descElem.Value.Trim();

        var pubElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "publisher");
        if (pubElem is not null) metadata.Publisher = pubElem.Value.Trim();

        var dateElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "date");
        if (dateElem is not null && DateTime.TryParse(dateElem.Value, System.Globalization.CultureInfo.InvariantCulture, out var pubDate))
        {
            metadata.Year = pubDate.Year;
            metadata.Month = pubDate.Month;
            metadata.Day = pubDate.Day;
        }

        foreach (var subject in doc.Descendants().Where(e => e.Name.LocalName == "subject"))
        {
            var val = subject.Value.Trim();
            if (!string.IsNullOrEmpty(val))
            {
                tags.Add(val);
            }
        }
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenAsync(cancellationToken);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    public static async Task<string?> FindOpfPathAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var container = archive.GetEntry("META-INF/container.xml");
        if (container is null)
        {
            return FirstOpfEntryName(archive);
        }

        try
        {
            var doc = await LoadXmlAsync(container, cancellationToken);
            var rootfile = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile");
            return rootfile?.Attribute("full-path")?.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FirstOpfEntryName(archive);
        }
    }

    private static string? FirstOpfEntryName(ZipArchive archive) =>
        archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?.FullName;

    public static async Task<ZipArchiveEntry?> FindCoverEntryAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var opfPath = await FindOpfPathAsync(archive, cancellationToken);
        if (!string.IsNullOrEmpty(opfPath))
        {
            var opfEntry = archive.GetEntry(opfPath);
            if (opfEntry is not null)
            {
                var entry = await TryFindCoverFromOpfAsync(archive, opfEntry, opfPath, cancellationToken);
                if (entry is not null) return entry;
            }
        }

        return FindFallbackCoverEntry(archive);
    }

    private static async Task<ZipArchiveEntry?> TryFindCoverFromOpfAsync(
        ZipArchive archive, ZipArchiveEntry opfEntry, string opfPath, CancellationToken cancellationToken)
    {
        try
        {
            var doc = await LoadXmlAsync(opfEntry, cancellationToken);
            var href = ExtractCoverHref(doc);

            if (!string.IsNullOrEmpty(href))
            {
                var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/');
                var fullHref = string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}";
                return archive.GetEntry(fullHref);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // OPF corrupto o sin portada declarada
        }

        return null;
    }

    private static string? ExtractCoverHref(XDocument doc) =>
        CoverHrefFromManifestProperties(doc) ?? CoverHrefFromCoverMeta(doc);

    private static string? CoverHrefFromManifestProperties(XDocument doc)
    {
        var coverItem = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "item" &&
            e.Attribute("properties")?.Value.Contains("cover-image") == true);

        var href = coverItem?.Attribute("href")?.Value;
        return string.IsNullOrEmpty(href) ? null : href;
    }

    private static string? CoverHrefFromCoverMeta(XDocument doc)
    {
        var coverMeta = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "meta" &&
            e.Attribute("name")?.Value == "cover");

        var coverId = coverMeta?.Attribute("content")?.Value;
        if (string.IsNullOrEmpty(coverId)) return null;

        var item = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "item" &&
            e.Attribute("id")?.Value == coverId);

        return item?.Attribute("href")?.Value;
    }

    private static ZipArchiveEntry? FindFallbackCoverEntry(ZipArchive archive)
    {
        return archive.Entries.FirstOrDefault(e =>
            {
                var name = Path.GetFileNameWithoutExtension(e.FullName).ToLowerInvariant();
                var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(e.FullName));
                return ct.IsImage() && (name == "cover" || name == "cover-image" || name.Contains("cover"));
            });
    }
}
