using System.Text.Json;
using System.Text.RegularExpressions;
using DiarSpeicher.Infrastructure.Filesystem.Metadata;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public sealed record BookAnalysisOptions
{
    public bool ComputeFileHash { get; init; } = true;
    public bool ComputeKoreaderHash { get; init; } = true;
    public bool ReadEmbeddedMetadata { get; init; } = true;
    public bool IncludeCover { get; init; }
    public bool MeasurePages { get; init; }

    public static readonly BookAnalysisOptions Default = new();
}

public interface IBookProcessor
{
    bool CanProcess(string extension);
    Task<ProcessedBook> AnalyzeBookAsync(string path, BookAnalysisOptions? options = null, CancellationToken cancellationToken = default);
    Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);

    Task<OpenedPage?> OpenPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);
}

public sealed record OpenedPage(ContentType ContentType, PageStream Content)
{
    public static async Task<OpenedPage?> FromBytesAsync(
        IBookProcessor processor,
        string path,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var page = await processor.ExtractPageAsync(path, pageNumber, cancellationToken);
        return page is null
            ? null
            : new OpenedPage(page.ContentType, PageStream.Detached(new MemoryStream(page.Data, writable: false)));
    }
}

internal static class CoverSelection
{
    public static int SelectCoverIndex(ExtractedMetadata? metadata, int pageCount)
    {
        var declared = metadata?.FrontCoverIndex;

        return declared is >= 0 && declared < pageCount ? declared.Value : 0;
    }
}

public class ZipBookProcessor : IBookProcessor
{
    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean is "cbz" or "zip";
    }

    public async Task<ProcessedBook> AnalyzeBookAsync(string path, BookAnalysisOptions? options = null, CancellationToken cancellationToken = default)
    {
        var analysis = options ?? BookAnalysisOptions.Default;
        var includeCover = analysis.IncludeCover;
        var measurePages = analysis.MeasurePages;
        ExtractedMetadata? metadata = null;
        List<string> tags = [];
        var pageCount = 0;
        ExtractedPage? cover = null;
        List<MeasuredPage> dimensions = [];

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

                if (analysis.ReadEmbeddedMetadata && string.Equals(Path.GetFileName(entryName), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
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
                cover = await ArchiveEntryReader.ReadAsync(ArchiveEntryRef.From(imageEntries[CoverSelection.SelectCoverIndex(metadata, imageEntries.Count)]), cancellationToken);
            }

            if (measurePages)
            {
                dimensions = await ArchiveEntryReader.MeasurePagesAsync(ArchiveEntryRef.From(imageEntries), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Corrupt archive or read error: proceed with 0 pages
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var diarSpeicherHash = analysis.ComputeFileHash
            ? await MediaHasher.ComputeDiarSpeicherHashAsync(path, length, cancellationToken)
            : null;

        return new ProcessedBook
        {
            Pages = pageCount,
            Hash = diarSpeicherHash,
            KoreaderHash = null,
            Metadata = metadata,
            Tags = tags,
            Cover = cover,
            PageDimensions = dimensions
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

            return await ArchiveEntryReader.ReadAsync(ArchiveEntryRef.From(imageEntries[targetIndex]), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<OpenedPage?> OpenPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return null;

        ZipArchive? archive = null;
        try
        {
            archive = await ZipFile.OpenReadAsync(path, cancellationToken);

            var imageEntries = archive.Entries
                .Where(e => !PathUtils.IsHiddenFile(e.FullName)
                    && ContentTypeExtensions.FromExtension(Path.GetExtension(e.FullName)).IsImage())
                .ToList();

            imageEntries.Sort(static (a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));

            if (pageNumber - 1 >= imageEntries.Count)
            {
                await archive.DisposeAsync();
                return null;
            }

            var entry = imageEntries[pageNumber - 1];
            var content = await entry.OpenAsync(cancellationToken);

            return new OpenedPage(
                ContentTypeExtensions.FromExtension(Path.GetExtension(entry.FullName)),
                PageStream.Owning(content, archive));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (archive is not null) await archive.DisposeAsync();
            return null;
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

    public async Task<ProcessedBook> AnalyzeBookAsync(string path, BookAnalysisOptions? options = null, CancellationToken cancellationToken = default)
    {
        var analysis = options ?? BookAnalysisOptions.Default;
        var includeCover = analysis.IncludeCover;
        var measurePages = analysis.MeasurePages;
        ExtractedMetadata? metadata = null;
        List<string> tags = [];
        var pageCount = 0;
        ExtractedPage? cover = null;
        List<MeasuredPage> dimensions = [];

        try
        {
            await using var archive = await RarArchive.OpenAsyncArchive(path, cancellationToken: cancellationToken);

            var imageEntries = new List<IArchiveEntry>();
            await foreach (var entry in archive.EntriesAsync.WithCancellation(cancellationToken))
            {
                var comicInfo = await ClassifyEntryAsync(entry, analysis, imageEntries, cancellationToken);
                if (comicInfo != null)
                {
                    (metadata, tags) = comicInfo.Value;
                }
            }

            imageEntries.Sort(static (a, b) => NaturalSortComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));
            pageCount = imageEntries.Count;

            if (includeCover && imageEntries.Count > 0)
            {
                cover = await ArchiveEntryReader.ReadAsync(ArchiveEntryRef.From(imageEntries[CoverSelection.SelectCoverIndex(metadata, imageEntries.Count)]), cancellationToken);
            }

            if (measurePages)
            {
                dimensions = await ArchiveEntryReader.MeasurePagesAsync(ArchiveEntryRef.From(imageEntries), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Corrupt archive or read error: proceed with 0 pages
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var diarSpeicherHash = analysis.ComputeFileHash
            ? await MediaHasher.ComputeDiarSpeicherHashAsync(path, length, cancellationToken)
            : null;

        return new ProcessedBook
        {
            Pages = pageCount,
            Hash = diarSpeicherHash,
            KoreaderHash = null,
            Metadata = metadata,
            Tags = tags,
            Cover = cover,
            PageDimensions = dimensions
        };
    }

    private static async Task<(ExtractedMetadata? Metadata, List<string> Tags)?> ClassifyEntryAsync(
        IArchiveEntry entry,
        BookAnalysisOptions analysis,
        List<IArchiveEntry> imageEntries,
        CancellationToken cancellationToken)
    {
        var key = entry.Key;
        if (entry.IsDirectory || string.IsNullOrEmpty(key) || PathUtils.IsHiddenFile(key))
        {
            return null;
        }

        if (analysis.ReadEmbeddedMetadata &&
            string.Equals(Path.GetFileName(key), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
        {
            await using var entryStream = await entry.OpenEntryStreamAsync(cancellationToken);
            using var streamReader = new StreamReader(entryStream);
            return ComicInfoParser.Parse(await streamReader.ReadToEndAsync(cancellationToken));
        }

        if (ContentTypeExtensions.FromExtension(Path.GetExtension(key)).IsImage())
        {
            imageEntries.Add(entry);
        }

        return null;
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

            return await ArchiveEntryReader.ReadAsync(ArchiveEntryRef.From(imageEntries[targetIndex]), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<OpenedPage?> OpenPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return null;

        IRarAsyncArchive? archive = null;
        try
        {
            archive = await RarArchive.OpenAsyncArchive(path, cancellationToken: cancellationToken);

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

            if (pageNumber - 1 >= imageEntries.Count)
            {
                await archive.DisposeAsync();
                return null;
            }

            var target = imageEntries[pageNumber - 1];
            var content = await target.OpenEntryStreamAsync(cancellationToken);

            return new OpenedPage(
                ContentTypeExtensions.FromExtension(Path.GetExtension(target.Key)),
                PageStream.Owning(content, archive));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (archive is not null) await archive.DisposeAsync();
            return null;
        }
    }
}

public class EpubBookProcessor : IBookProcessor
{
    private readonly IEpubProfileProvider? _profileProvider;

    public EpubBookProcessor(IEpubProfileProvider? profileProvider = null)
    {
        _profileProvider = profileProvider;
    }

    public bool CanProcess(string extension)
    {
        var clean = extension.TrimStart('.').ToLowerInvariant();
        return clean == "epub";
    }

    public async Task<ProcessedBook> AnalyzeBookAsync(string path, BookAnalysisOptions? options = null, CancellationToken cancellationToken = default)
    {
        var analysis = options ?? BookAnalysisOptions.Default;
        var includeCover = analysis.IncludeCover;
        var measurePages = analysis.MeasurePages;
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
                    cover = await ArchiveEntryReader.ReadAsync(ArchiveEntryRef.From(coverEntry), cancellationToken);
                }
            }
        }

        var fileInfo = new FileInfo(path);
        var length = fileInfo.Exists ? fileInfo.Length : 0;
        var diarSpeicherHash = analysis.ComputeFileHash
            ? await MediaHasher.ComputeDiarSpeicherHashAsync(path, length, cancellationToken)
            : null;
        var koreaderHash = analysis.ComputeKoreaderHash
            ? await MediaHasher.ComputeKoreaderHashAsync(path, cancellationToken)
            : null;

        return new ProcessedBook
        {
            Pages = Math.Max(1, chapterCount),
            Hash = diarSpeicherHash,
            KoreaderHash = koreaderHash,
            Metadata = analysis.ReadEmbeddedMetadata ? metadata : null,
            Tags = analysis.ReadEmbeddedMetadata ? tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [],
            Cover = cover
        };
    }

    public async Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1) return null;

        await using var archive = await ZipFile.OpenReadAsync(path, cancellationToken);
        var coverEntry = await FindCoverEntryAsync(archive, cancellationToken);

        var spineEntries = await GetSpineEntriesAsync(archive, cancellationToken);
        if (spineEntries.Count == 0)
        {
            spineEntries = GetFallbackHtmlEntries(archive);
        }

        if (spineEntries.Count == 0)
        {
            return (pageNumber == 1 && coverEntry != null) ? await ArchiveEntryReader.ReadAsync(ArchiveEntryRef.From(coverEntry), cancellationToken) : null;
        }

        var profile = _profileProvider != null
            ? await _profileProvider.GetCurrentProfileAsync(cancellationToken)
            : EpubDeviceProfile.GetDefaults()[0];

        var map = await EpubRasterizer.GetOrBuildPageMapAsync(
            archive,
            path,
            spineEntries,
            coverEntry,
            profile,
            cancellationToken);

        if (pageNumber < 1 || pageNumber > map.TotalPages)
        {
            return null;
        }

        var target = map.Pages[pageNumber - 1];
        var bookTitle = Path.GetFileNameWithoutExtension(path);

        return await EpubRasterizer.RenderSubpageAsync(
            archive,
            target,
            pageNumber,
            map.TotalPages,
            bookTitle,
            profile,
            cancellationToken);
    }

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
        var itemrefs = spine.Descendants().Where(e => e.Name.LocalName == "itemref");

        foreach (var itemref in itemrefs)
        {
            var idref = itemref.Attribute("idref")?.Value;
            if (string.IsNullOrEmpty(idref) || !manifest.TryGetValue(idref, out var href)) continue;

            var fullHref = string.IsNullOrEmpty(opfDir) ? href : $"{opfDir}/{href}";
            var entry = archive.GetEntry(fullHref) ??
                archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), fullHref.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public static List<ZipArchiveEntry> GetFallbackHtmlEntries(ZipArchive archive)
    {
        return archive.Entries
            .Where(e =>
            {
                var ext = Path.GetExtension(e.FullName).ToLowerInvariant();
                return ext is ".html" or ".xhtml";
            })
            .OrderBy(e => e.FullName, NaturalSortComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
            // OPF corrupto o sin portada declarada: devolver null hace que el llamante
            // busque la portada por nombre de fichero.
        }

        return null;
    }

    private static string? ExtractCoverHref(XDocument doc)
    {
        var coverItem = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "item" &&
            e.Attribute("properties")?.Value.Contains("cover-image") == true);

        var href = coverItem?.Attribute("href")?.Value;
        if (!string.IsNullOrEmpty(href))
        {
            return href;
        }

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

    public Task<OpenedPage?> OpenPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default) =>
        OpenedPage.FromBytesAsync(this, path, pageNumber, cancellationToken);
}

public interface ICompositeBookProcessor
{
    IBookProcessor? GetProcessor(string path);
    Task<ProcessedBook> AnalyzeAsync(string path, BookAnalysisOptions? options = null, CancellationToken cancellationToken = default);
    Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);
    Task<OpenedPage?> OpenPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default);
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

    public async Task<ProcessedBook> AnalyzeAsync(string path, BookAnalysisOptions? options = null, CancellationToken cancellationToken = default)
    {
        var analysis = options ?? BookAnalysisOptions.Default;
        var processor = GetProcessor(path);
        if (processor is null)
        {
            var fileInfo = new FileInfo(path);
            var len = fileInfo.Exists ? fileInfo.Length : 0;
            return new ProcessedBook
            {
                Pages = 0,
                Hash = analysis.ComputeFileHash ? await MediaHasher.ComputeDiarSpeicherHashAsync(path, len, cancellationToken) : null,
                KoreaderHash = analysis.ComputeKoreaderHash ? await MediaHasher.ComputeKoreaderHashAsync(path, cancellationToken) : null
            };
        }

        return await processor.AnalyzeBookAsync(path, analysis, cancellationToken);
    }

    public Task<OpenedPage?> OpenPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        var processor = GetProcessor(path);
        return processor is null
            ? Task.FromResult<OpenedPage?>(null)
            : processor.OpenPageAsync(path, pageNumber, cancellationToken);
    }

    public Task<ExtractedPage?> ExtractPageAsync(string path, int pageNumber, CancellationToken cancellationToken = default)
    {
        var processor = GetProcessor(path);
        return processor is null
            ? Task.FromResult<ExtractedPage?>(null)
            : processor.ExtractPageAsync(path, pageNumber, cancellationToken);
    }
}
