using System.Collections.Frozen;

namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Lectura del interior de un EPUB para el lector: indice de contenidos y recursos sueltos.
/// </summary>
public sealed partial class DiarSpeicherService
{
    private static async Task<List<DiarSpeicherEpubTocItem>> ReadNcxTocAsync(ZipArchive archive, CancellationToken ct)
    {
        var tocItems = new List<DiarSpeicherEpubTocItem>();

        var ncxEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));
        if (ncxEntry == null) return tocItems;

        await using var stream = await ncxEntry.OpenAsync(ct);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        foreach (var point in doc.Descendants().Where(e => e.Name.LocalName == "navPoint"))
        {
            var label = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value.Trim();
            var contentSrc = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;

            if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(contentSrc))
            {
                tocItems.Add(new DiarSpeicherEpubTocItem { Title = label, Href = contentSrc });
            }
        }

        return tocItems;
    }

    private static List<DiarSpeicherEpubTocItem> BuildSequentialToc(ZipArchive archive)
    {
        var htmlEntries = archive.Entries
            .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, NaturalSortComparer.OrdinalIgnoreCase)
            .ToList();

        return [.. htmlEntries.Select((e, i) => new DiarSpeicherEpubTocItem { Title = $"Section {i + 1}", Href = e.FullName })];
    }

    public async Task<DiarSpeicherEpubTocDto?> GetEpubTocAsync(AuthUser user, string mediaId, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .Include(m => m.Metadata)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var ext = media.Extension.TrimStart('.').ToLowerInvariant();
        if (ext != "epub") return null;

        await using var fileStream = new FileStream(media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var archive = await ZipArchive.CreateAsync(fileStream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, ct);
        var tocItems = await ReadNcxTocAsync(archive, ct);
        if (tocItems.Count == 0)
        {
            tocItems = BuildSequentialToc(archive);
        }

        return new DiarSpeicherEpubTocDto
        {
            MediaId = media.Id,
            Title = media.Metadata?.Title ?? media.Name,
            Items = tocItems
        };
    }

    public async Task<(byte[] Data, string ContentType)?> GetEpubResourceAsync(AuthUser user, string mediaId, string resourcePath, CancellationToken ct = default)
    {
        var media = await _db.Media.ForUser(user)
            .FirstOrDefaultAsync(m => m.Id == mediaId, ct);

        if (media == null || !File.Exists(media.Path)) return null;

        var cleanPath = resourcePath.TrimStart('/').Split('#')[0];

        await using var fileStream = new FileStream(media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using var archive = await ZipArchive.CreateAsync(fileStream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, ct);
        var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(cleanPath, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(Path.GetFileName(cleanPath), StringComparison.OrdinalIgnoreCase));

        if (entry == null) return null;

        await using var stream = await entry.OpenAsync(ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        var contentType = GetEpubResourceContentType(Path.GetExtension(entry.FullName));
        return (ms.ToArray(), contentType);
    }

    private static readonly FrozenDictionary<string, string> EpubResourceMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "application/xhtml+xml",
            [".xhtml"] = "application/xhtml+xml",
            [".css"] = "text/css",
            [".js"] = "application/javascript",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".ncx"] = "application/x-dtbncx+xml",
            [".opf"] = "application/oebps-package+xml",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static string GetEpubResourceContentType(string extension) =>
        EpubResourceMimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
}
