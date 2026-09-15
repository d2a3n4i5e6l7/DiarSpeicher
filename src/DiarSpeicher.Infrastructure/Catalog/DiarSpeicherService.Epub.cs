namespace DiarSpeicher.Infrastructure.Catalog;

/// <summary>
/// Lectura del interior de un EPUB para el lector: indice de contenidos y recursos sueltos.
/// </summary>
public sealed partial class DiarSpeicherService
{
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
        var tocItems = new List<DiarSpeicherEpubTocItem>();

        var ncxEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));
        if (ncxEntry != null)
        {
            await using var stream = await ncxEntry.OpenAsync(ct);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            var navPoints = doc.Descendants().Where(e => e.Name.LocalName == "navPoint");

            foreach (var point in navPoints)
            {
                var label = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value.Trim();
                var contentSrc = point.Descendants().FirstOrDefault(e => e.Name.LocalName == "content")?.Attribute("src")?.Value;

                if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(contentSrc))
                {
                    tocItems.Add(new DiarSpeicherEpubTocItem { Title = label, Href = contentSrc });
                }
            }
        }

        if (tocItems.Count == 0)
        {
            var htmlEntries = archive.Entries
                .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, NaturalSortComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < htmlEntries.Count; i++)
            {
                tocItems.Add(new DiarSpeicherEpubTocItem { Title = $"Section {i + 1}", Href = htmlEntries[i].FullName });
            }
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

        var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".html" or ".xhtml" => "application/xhtml+xml",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".ncx" => "application/x-dtbncx+xml",
            ".opf" => "application/oebps-package+xml",
            _ => "application/octet-stream"
        };

        return (ms.ToArray(), contentType);
    }
}
