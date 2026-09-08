using System.IO.Compression;
using System.Xml.Linq;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public interface IBookProcessor
{
    bool CanProcess(string extension);

    /// <summary>
    /// Analyzes a book. When <paramref name="includeCover"/> is set the cover page is
    /// returned in <see cref="ProcessedBook.Cover"/>, so callers that need both metadata
    /// and a thumbnail only open and decompress the archive once.
    /// </summary>
    Task<ProcessedBook> AnalyzeBookAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default);

    Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);
}

public class ZipBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean is "cbz" or "zip";
    }

    public async Task<ProcessedBook> AnalyzeBookAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default)
    {
        ExtractedMetadata? metadata = null;
        List<string> tags = [];
        var pageCount = 0;
        ExtractedPage? cover = null;

        try
        {
            await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
            var imageEntries = new List<ZipArchiveEntry>();

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entryName = entry.FullName;
                if (PathUtils.IsHiddenFile(entryName))
                {
                    continue;
                }

                var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(entryName));

                if (string.Equals(Path.GetFileName(entryName), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                {
                    await using var stream = await entry.OpenAsync(cancellationToken);
                    using var reader = new StreamReader(stream);
                    var xmlContent = await reader.ReadToEndAsync(cancellationToken);
                    (metadata, tags) = ComicInfoParser.Parse(xmlContent);
                }
                else if (ct.IsImage())
                {
                    imageEntries.Add(entry);
                }
            }

            imageEntries.Sort(static (a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));
            pageCount = imageEntries.Count;

            if (includeCover && imageEntries.Count > 0)
            {
                cover = await ReadEntryAsync(imageEntries[0], cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Corrupt archive or read error: proceed with 0 pages
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var stumpHash = await MediaHasher.ComputeStumpHashAsync(path, length, cancellationToken);

        return new ProcessedBook
        {
            Pages = pageCount,
            Hash = stumpHash,
            KoreaderHash = null,
            Metadata = metadata,
            Tags = tags,
            Cover = cover
        };
    }

    public async Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return null;

        try
        {
            await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
            var imageEntries = new List<ZipArchiveEntry>();

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (PathUtils.IsHiddenFile(entry.FullName))
                    continue;

                var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(entry.FullName));
                if (ct.IsImage())
                {
                    imageEntries.Add(entry);
                }
            }

            imageEntries.Sort(static (a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));

            int targetIndex = pageNumber - 1;
            if (targetIndex >= imageEntries.Count)
            {
                return null;
            }

            return await ReadEntryAsync(imageEntries[targetIndex], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<ExtractedPage> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenAsync(cancellationToken);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);

        var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(entry.FullName));
        return new ExtractedPage(ct, ms.ToArray());
    }
}

public class RarBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean is "cbr" or "rar";
    }

    public async Task<ProcessedBook> AnalyzeBookAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default)
    {
        ExtractedMetadata? metadata = null;
        List<string> tags = [];
        var pageCount = 0;
        ExtractedPage? cover = null;

        try
        {
            await using var archive = await RarArchive.OpenAsyncArchive(path, cancellationToken: cancellationToken);
            var imageEntries = new List<IArchiveEntry>();

            await foreach (var entry in archive.EntriesAsync.WithCancellation(cancellationToken))
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key) || PathUtils.IsHiddenFile(key)) continue;

                if (string.Equals(Path.GetFileName(key), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                {
                    await using var entryStream = await entry.OpenEntryStreamAsync(cancellationToken);
                    using var streamReader = new StreamReader(entryStream);
                    var xmlContent = await streamReader.ReadToEndAsync(cancellationToken);
                    (metadata, tags) = ComicInfoParser.Parse(xmlContent);
                }
                else if (ContentTypeExtensions.FromExtension(Path.GetExtension(key)).IsImage())
                {
                    imageEntries.Add(entry);
                }
            }

            imageEntries.Sort(static (a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
            pageCount = imageEntries.Count;

            if (includeCover && imageEntries.Count > 0)
            {
                cover = await ReadEntryAsync(imageEntries[0], cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Corrupt archive or read error: proceed with 0 pages
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var stumpHash = await MediaHasher.ComputeStumpHashAsync(path, length, cancellationToken);

        return new ProcessedBook
        {
            Pages = pageCount,
            Hash = stumpHash,
            KoreaderHash = null,
            Metadata = metadata,
            Tags = tags,
            Cover = cover
        };
    }

    public async Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return null;

        try
        {
            await using var archive = await RarArchive.OpenAsyncArchive(path, cancellationToken: cancellationToken);

            var imageEntries = new List<IArchiveEntry>();
            await foreach (var entry in archive.EntriesAsync.WithCancellation(cancellationToken))
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key;
                if (string.IsNullOrEmpty(key) || PathUtils.IsHiddenFile(key)) continue;

                if (ContentTypeExtensions.FromExtension(Path.GetExtension(key)).IsImage())
                {
                    imageEntries.Add(entry);
                }
            }

            imageEntries.Sort(static (a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));

            var targetIndex = pageNumber - 1;
            if (targetIndex >= imageEntries.Count)
            {
                return null;
            }

            return await ReadEntryAsync(imageEntries[targetIndex], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<ExtractedPage> ReadEntryAsync(IArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var entryStream = await entry.OpenEntryStreamAsync(cancellationToken);
        using var ms = new MemoryStream();
        await entryStream.CopyToAsync(ms, cancellationToken);

        var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(entry.Key));
        return new ExtractedPage(ct, ms.ToArray());
    }
}

public class EpubBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean == "epub";
    }

    public async Task<ProcessedBook> AnalyzeBookAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default)
    {
        var metadata = new ExtractedMetadata();
        var tags = new List<string>();
        int chapterCount = 0;
        ExtractedPage? cover = null;

        await using (var archive = await ZipFile.OpenReadAsync(path, cancellationToken))
        {
            var opfPath = await FindOpfPathAsync(archive, cancellationToken);
            if (!string.IsNullOrEmpty(opfPath))
            {
                var opfEntry = archive.GetEntry(opfPath);
                if (opfEntry is not null)
                {
                    chapterCount = await ReadOpfDataAsync(opfEntry, metadata, tags, cancellationToken);
                }
            }

            if (chapterCount == 0)
            {
                chapterCount = CountFallbackHtmlEntries(archive);
            }

            if (includeCover)
            {
                var coverEntry = await FindCoverEntryAsync(archive, cancellationToken);
                if (coverEntry is not null)
                {
                    cover = await ReadEntryAsync(coverEntry, cancellationToken);
                }
            }
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var stumpHash = await MediaHasher.ComputeStumpHashAsync(path, length, cancellationToken);
        var koreaderHash = await MediaHasher.ComputeKoreaderHashAsync(path, cancellationToken);

        return new ProcessedBook
        {
            Pages = Math.Max(1, chapterCount),
            Hash = stumpHash,
            KoreaderHash = koreaderHash,
            Metadata = metadata,
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Cover = cover
        };
    }

    public async Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        // For EPUB, page 1 is the cover image
        await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
        var coverEntry = await FindCoverEntryAsync(archive, cancellationToken);
        if (coverEntry is null)
        {
            return null;
        }

        return await ReadEntryAsync(coverEntry, cancellationToken);
    }

    private static async Task<ExtractedPage> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenAsync(cancellationToken);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);

        var ct = ContentTypeExtensions.FromExtension(Path.GetExtension(entry.FullName));
        return new ExtractedPage(ct, ms.ToArray());
    }

    /// <summary>Returns the chapter count found in the OPF spine.</summary>
    private static async Task<int> ReadOpfDataAsync(
        ZipArchiveEntry opfEntry, ExtractedMetadata metadata, List<string> tags, CancellationToken cancellationToken)
    {
        var doc = await LoadXmlAsync(opfEntry, cancellationToken);

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
        var chapterCount = spineElem?.Descendants().Count(e => e.Name.LocalName == "itemref") ?? 0;
        if (chapterCount == 0)
        {
            chapterCount = doc.Descendants().Count(e => e.Name.LocalName == "itemref");
        }

        return chapterCount;
    }

    private static int CountFallbackHtmlEntries(ZipArchive archive)
    {
        return archive.Entries.Count(e =>
        {
            var ext = Path.GetExtension(e.FullName).ToLowerInvariant();
            return ext is ".html" or ".xhtml";
        });
    }

    private static async Task<XDocument> LoadXmlAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = await entry.OpenAsync(cancellationToken);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    private static async Task<string?> FindOpfPathAsync(ZipArchive archive, CancellationToken cancellationToken)
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

    private static async Task<ZipArchiveEntry?> FindCoverEntryAsync(ZipArchive archive, CancellationToken cancellationToken)
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
    Task<ProcessedBook> AnalyzeAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default);
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

    public async Task<ProcessedBook> AnalyzeAsync(string path, bool includeCover = false, CancellationToken cancellationToken = default)
    {
        var processor = GetProcessor(path);
        if (processor is null)
        {
            var fileInfo = new FileInfo(path);
            var len = fileInfo.Exists ? fileInfo.Length : 0;
            return new ProcessedBook
            {
                Pages = 0,
                Hash = await MediaHasher.ComputeStumpHashAsync(path, len, cancellationToken),
                KoreaderHash = await MediaHasher.ComputeKoreaderHashAsync(path, cancellationToken)
            };
        }

        return await processor.AnalyzeBookAsync(path, includeCover, cancellationToken);
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        var processor = GetProcessor(path);
        return processor is null
            ? Task.FromResult<ExtractedPage?>(null)
            : processor.ExtractPageAsync(path, pageNumber, cancellationToken);
    }
}
