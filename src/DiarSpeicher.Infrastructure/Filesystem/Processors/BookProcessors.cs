using System.IO.Compression;
using System.Xml.Linq;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;
using SharpCompress.Archives.Rar;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public interface IBookProcessor
{
    bool CanProcess(string extension);
    Task<ProcessedBook> AnalyzeBookAsync(string path, CancellationToken cancellationToken = default);
    Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);
}

public class ZipBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean is "cbz" or "zip";
    }

    public Task<ProcessedBook> AnalyzeBookAsync(string path, CancellationToken cancellationToken = default)
    {
        ExtractedMetadata? metadata = null;
        List<string> tags = [];
        var imageEntries = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                var entryName = entry.FullName;
                if (PathUtils.IsHiddenFile(entryName))
                {
                    continue;
                }

                var ext = Path.GetExtension(entryName);
                var ct = ContentTypeExtensions.FromExtension(ext);

                if (string.Equals(Path.GetFileName(entryName), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    var xmlContent = reader.ReadToEnd();
                    (metadata, tags) = ComicInfoParser.Parse(xmlContent);
                }
                else if (ct.IsImage())
                {
                    imageEntries.Add(entryName);
                }
            }

            imageEntries.Sort(NaturalSortComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Corrupt archive or read error: proceed with 0 pages
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var stumpHash = MediaHasher.ComputeStumpHash(path, length);

        var result = new ProcessedBook
        {
            Pages = imageEntries.Count,
            Hash = stumpHash,
            KoreaderHash = null,
            Metadata = metadata,
            Tags = tags
        };

        return Task.FromResult(result);
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return Task.FromResult<ExtractedPage?>(null);

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var imageEntries = new List<ZipArchiveEntry>();

            foreach (var entry in archive.Entries)
            {
                if (PathUtils.IsHiddenFile(entry.FullName))
                    continue;

                var ext = Path.GetExtension(entry.FullName);
                var ct = ContentTypeExtensions.FromExtension(ext);
                if (ct.IsImage())
                {
                    imageEntries.Add(entry);
                }
            }

            imageEntries.Sort((a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));

            int targetIndex = pageNumber - 1;
            if (targetIndex >= imageEntries.Count)
            {
                return Task.FromResult<ExtractedPage?>(null);
            }

            var targetEntry = imageEntries[targetIndex];
            using var stream = targetEntry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            var ctTarget = ContentTypeExtensions.FromExtension(Path.GetExtension(targetEntry.FullName));
            return Task.FromResult<ExtractedPage?>(new ExtractedPage(ctTarget, ms.ToArray()));
        }
        catch (Exception)
        {
            return Task.FromResult<ExtractedPage?>(null);
        }
    }
}

public class RarBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean is "cbr" or "rar";
    }

    public Task<ProcessedBook> AnalyzeBookAsync(string path, CancellationToken cancellationToken = default)
    {
        ExtractedMetadata? metadata = null;
        List<string> tags = [];
        var imageNames = new List<string>();

        using (var archive = RarArchive.OpenArchive(path))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key) || PathUtils.IsHiddenFile(key)) continue;

                if (string.Equals(Path.GetFileName(key), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var streamReader = new StreamReader(entryStream);
                    var xmlContent = streamReader.ReadToEnd();
                    (metadata, tags) = ComicInfoParser.Parse(xmlContent);
                }
                else
                {
                    var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(key));
                    if (ct.IsImage())
                    {
                        imageNames.Add(key);
                    }
                }
            }
        }

        imageNames.Sort(NaturalSortComparer.OrdinalIgnoreCase);

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var stumpHash = MediaHasher.ComputeStumpHash(path, length);

        var result = new ProcessedBook
        {
            Pages = imageNames.Count,
            Hash = stumpHash,
            KoreaderHash = null,
            Metadata = metadata,
            Tags = tags
        };

        return Task.FromResult(result);
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return Task.FromResult<ExtractedPage?>(null);

        using var archive = RarArchive.OpenArchive(path);

        var imageEntries = archive.Entries
            .Where(e => !e.IsDirectory
                        && !string.IsNullOrEmpty(e.Key)
                        && !PathUtils.IsHiddenFile(e.Key)
                        && ContentTypeExtensions.FromExtension(Path.GetExtension(e.Key)).IsImage())
            .OrderBy(e => e.Key, NaturalSortComparer.OrdinalIgnoreCase)
            .ToList();

        var targetIndex = pageNumber - 1;
        if (targetIndex >= imageEntries.Count)
        {
            return Task.FromResult<ExtractedPage?>(null);
        }

        var targetEntry = imageEntries[targetIndex];

        using var entryStream = targetEntry.OpenEntryStream();
        using var ms = new MemoryStream();
        entryStream.CopyTo(ms);

        var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(targetEntry.Key));
        return Task.FromResult<ExtractedPage?>(new ExtractedPage(ct, ms.ToArray()));
    }
}

public class EpubBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean == "epub";
    }

    public Task<ProcessedBook> AnalyzeBookAsync(string path, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(path);
        var metadata = new ExtractedMetadata();
        var tags = new List<string>();
        int chapterCount = 0;

        var opfPath = FindOpfPath(archive);
        if (!string.IsNullOrEmpty(opfPath))
        {
            var opfEntry = archive.GetEntry(opfPath);
            if (opfEntry is not null)
            {
                ReadOpfData(opfEntry, metadata, tags, ref chapterCount);
            }
        }

        if (chapterCount == 0)
        {
            chapterCount = CountFallbackHtmlEntries(archive);
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var stumpHash = MediaHasher.ComputeStumpHash(path, length);
        var koreaderHash = MediaHasher.ComputeKoreaderHash(path);

        var result = new ProcessedBook
        {
            Pages = Math.Max(1, chapterCount),
            Hash = stumpHash,
            KoreaderHash = koreaderHash,
            Metadata = metadata,
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        return Task.FromResult(result);
    }

    private static void ReadOpfData(ZipArchiveEntry opfEntry, ExtractedMetadata metadata, List<string> tags, ref int chapterCount)
    {
        using var stream = opfEntry.Open();
        var doc = XDocument.Load(stream);

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

        var spineElem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "spine");
        chapterCount = spineElem?.Descendants().Count(e => e.Name.LocalName == "itemref") ?? 0;
        if (chapterCount == 0)
        {
            chapterCount = doc.Descendants().Count(e => e.Name.LocalName == "itemref");
        }
    }

    private static int CountFallbackHtmlEntries(ZipArchive archive)
    {
        return archive.Entries.Count(e =>
        {
            var ext = Path.GetExtension(e.FullName).ToLowerInvariant();
            return ext is ".html" or ".xhtml";
        });
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        // For EPUB, page 1 is the cover image
        using var archive = ZipFile.OpenRead(path);
        var coverEntry = FindCoverEntry(archive);
        if (coverEntry is null)
        {
            return Task.FromResult<ExtractedPage?>(null);
        }

        using var stream = coverEntry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(coverEntry.FullName));
        return Task.FromResult<ExtractedPage?>(new ExtractedPage(ct, ms.ToArray()));
    }

    private static string? FindOpfPath(ZipArchive archive)
    {
        var container = archive.GetEntry("META-INF/container.xml");
        if (container is null)
        {
            return archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?.FullName;
        }

        try
        {
            using var stream = container.Open();
            var doc = XDocument.Load(stream);
            var rootfile = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile");
            return rootfile?.Attribute("full-path")?.Value;
        }
        catch
        {
            return archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))?.FullName;
        }
    }

    private static ZipArchiveEntry? FindCoverEntry(ZipArchive archive)
    {
        var opfPath = FindOpfPath(archive);
        if (!string.IsNullOrEmpty(opfPath))
        {
            var opfEntry = archive.GetEntry(opfPath);
            if (opfEntry is not null)
            {
                var entry = TryFindCoverFromOpf(archive, opfEntry, opfPath);
                if (entry is not null) return entry;
            }
        }

        return FindFallbackCoverEntry(archive);
    }

    private static ZipArchiveEntry? TryFindCoverFromOpf(ZipArchive archive, ZipArchiveEntry opfEntry, string opfPath)
    {
        try
        {
            using var stream = opfEntry.Open();
            var doc = XDocument.Load(stream);
            var href = ExtractCoverHref(doc);

            if (!string.IsNullOrEmpty(href))
            {
                var opfDir = Path.GetDirectoryName(opfPath)?.Replace('\\', '/');
                var fullHref = string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}";
                return archive.GetEntry(fullHref);
            }
        }
        catch
        {
            // Fallback to name search
        }

        return null;
    }

    private static string? ExtractCoverHref(XDocument doc)
    {
        // 1. EPUB3: <item properties="cover-image" href="...">
        var coverItem = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "item" &&
            e.Attribute("properties")?.Value.Contains("cover-image") == true);

        var href = coverItem?.Attribute("href")?.Value;
        if (!string.IsNullOrEmpty(href))
        {
            return href;
        }

        // 2. EPUB2: <meta name="cover" content="cover-id"/> -> <item id="cover-id" href="...">
        var coverMeta = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "meta" &&
            e.Attribute("name")?.Value == "cover");

        var coverId = coverMeta?.Attribute("content")?.Value;
        if (!string.IsNullOrEmpty(coverId))
        {
            var item = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "item" &&
                e.Attribute("id")?.Value == coverId);
            return item?.Attribute("href")?.Value;
        }

        return null;
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

public interface ICompositeBookProcessor
{
    IBookProcessor? GetProcessor(string path);
    Task<ProcessedBook> AnalyzeAsync(string path, CancellationToken cancellationToken = default);
    Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);
}

public class CompositeBookProcessor : ICompositeBookProcessor
{
    private readonly IEnumerable<IBookProcessor> _processors;

    public CompositeBookProcessor(IEnumerable<IBookProcessor> processors)
    {
        _processors = processors;
    }

    public IBookProcessor? GetProcessor(string path)
    {
        var ext = Path.GetExtension(path);
        return _processors.FirstOrDefault(p => p.CanProcess(ext));
    }

    public async Task<ProcessedBook> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        var processor = GetProcessor(path);
        if (processor is null)
        {
            var fileInfo = new FileInfo(path);
            var len = fileInfo.Exists ? fileInfo.Length : 0;
            return new ProcessedBook
            {
                Pages = 0,
                Hash = MediaHasher.ComputeStumpHash(path, len),
                KoreaderHash = MediaHasher.ComputeKoreaderHash(path)
            };
        }

        return await processor.AnalyzeBookAsync(path, cancellationToken);
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        var processor = GetProcessor(path);
        return processor is null
            ? Task.FromResult<ExtractedPage?>(null)
            : processor.ExtractPageAsync(path, pageNumber, cancellationToken);
    }
}
