using System.Collections.Concurrent;
using AngleSharp.Html.Parser;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public record EpubSubpageTarget(
    string EntryFullName,
    int ChapterSpineIndex,
    int SubpageIndex,
    int TotalSubpagesInChapter,
    bool IsImageOnly,
    int? ImageWidth = null,
    int? ImageHeight = null
);

public class EpubBookPageMap
{
    public string BookPath { get; init; } = "";
    public int TotalPages => Pages.Count;
    public List<EpubSubpageTarget> Pages { get; init; } = [];
}

public static partial class EpubRasterizer
{
    private static readonly ConcurrentDictionary<string, (EpubBookPageMap Map, DateTime CachedAt, DateTime FileWrittenAt)> PageMapCache = new();

    private const int PageMapCacheLimit = 256;

    private static readonly Regex ImgTagRegex = new(@"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex SvgImageRegex = new(@"<image[^>]+(?:href|xlink:href)=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    public record RenderBlock(string Text, bool IsHeading, int HeadingLevel);

    public record ThemeColors(SKColor Bg, SKColor Text, SKColor Heading, SKColor Muted, SKColor Accent);

    public record PageMeta(int PageNumber, int TotalPages, string? BookTitle, int ChapterIndex = 0, int SubpageIndex = 0, int TotalSubpages = 1);

    public record LayoutMetrics(float ContentWidth, float LineHeight, float HeadingLineHeight, float ParagraphSpacing, float MarginY);

    public record PageGeometry(
        int Width,
        int MarginX,
        int MarginY,
        float ContentWidth,
        int BaseFontSize,
        float LineHeight,
        float HeadingFontSize,
        float HeadingLineHeight,
        float ParagraphSpacing,
        float ContentTop,
        float AvailableHeight)
    {
        public const float HeaderHeight = 110f;
        public const float FooterHeight = 90f;

        public static PageGeometry For(EpubDeviceProfile profile)
        {
            var width = Math.Max(480, profile.Width);
            var marginX = Math.Max(24, profile.MarginHorizontal);
            var marginY = Math.Max(40, profile.MarginVertical);
            var baseFontSize = Math.Max(16, profile.FontSize);
            var headingFontSize = baseFontSize * 1.35f;

            return new PageGeometry(
                width,
                marginX,
                marginY,
                width - (marginX * 2),
                baseFontSize,
                baseFontSize * Math.Max(1.2f, profile.LineHeight),
                headingFontSize,
                headingFontSize * 1.3f,
                baseFontSize * 0.75f,
                marginY + HeaderHeight,
                Math.Max(300f, profile.Height - (marginY * 2) - HeaderHeight - FooterHeight));
        }

        public LayoutMetrics ToMetrics() =>
            new(ContentWidth, LineHeight, HeadingLineHeight, ParagraphSpacing, MarginY);
    }

    public static async Task<EpubBookPageMap> GetOrBuildPageMapAsync(
        ZipArchive archive,
        string epubPath,
        List<ZipArchiveEntry> spineEntries,
        ZipArchiveEntry? coverEntry,
        EpubDeviceProfile profile,
        CancellationToken ct = default)
    {
        var cacheKey = $"{epubPath}:{BuildProfileKey(profile)}";
        var fileWrittenAt = GetFileWrittenAtUtc(epubPath);
        if (PageMapCache.TryGetValue(cacheKey, out var cached)
            && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 60
            && cached.FileWrittenAt == fileWrittenAt)
        {
            return cached.Map;
        }

        var geometry = PageGeometry.For(profile);
        var marginY = geometry.MarginY;

        var fonts = EpubFontProvider.Resolve(profile, archive);
        using var textFont = new SKFont(fonts.Regular, geometry.BaseFontSize) { Subpixel = true };
        using var headingFont = new SKFont(fonts.Bold, geometry.HeadingFontSize) { Subpixel = true };

        var metrics = geometry.ToMetrics();

        const float headerHeight = PageGeometry.HeaderHeight;
        var availableHeight = geometry.AvailableHeight;

        var targets = new List<EpubSubpageTarget>();

        if (coverEntry != null && !spineEntries.Any(s => string.Equals(s.FullName, coverEntry.FullName, StringComparison.OrdinalIgnoreCase)))
        {
            var (w, h) = await TryMeasureEntryAsync(coverEntry, ct);
            targets.Add(new EpubSubpageTarget(coverEntry.FullName, 0, 0, 1, IsImageOnly: true, w, h));
        }

        for (var i = 0; i < spineEntries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chapter = spineEntries[i];

            string html;
            await using (var stream = await chapter.OpenAsync(ct))
            using (var reader = new StreamReader(stream))
            {
                html = await reader.ReadToEndAsync(ct);
            }

            var blocks = ExtractBlocks(html);
            var imageOnly = await TryBuildImageOnlyTargetAsync(archive, chapter, html, blocks, i + 1, ct);
            if (imageOnly != null)
            {
                targets.Add(imageOnly);
                continue;
            }

            var (_, totalContentHeight) = MeasureLayout(blocks, textFont, headingFont, metrics);
            var usableContentHeight = Math.Max(1f, totalContentHeight - headerHeight - marginY);
            var subpageCount = Math.Max(1, (int)Math.Ceiling(usableContentHeight / availableHeight));
            for (var p = 0; p < subpageCount; p++)
            {
                targets.Add(new EpubSubpageTarget(chapter.FullName, i + 1, p, subpageCount, IsImageOnly: false));
            }
        }

        var map = new EpubBookPageMap { BookPath = epubPath, Pages = targets };
        TrimPageMapCache();
        PageMapCache[cacheKey] = (map, DateTime.UtcNow, fileWrittenAt);
        return map;
    }

    public static string BuildProfileKey(EpubDeviceProfile profile) =>
        $"{profile.Width}x{profile.Height}:{profile.FontSize}:{profile.LineHeight}:{profile.MarginHorizontal}:{profile.MarginVertical}:{profile.FontFamily}:{profile.Theme}";

    private static DateTime GetFileWrittenAtUtc(string epubPath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(epubPath);
        }
        catch
        {
            // Without a date there is no way to know whether the cached map is still valid:
            // MinValue never passes the check, so the map is rebuilt.
            return DateTime.MinValue;
        }
    }

    private static void TrimPageMapCache()
    {
        if (PageMapCache.Count < PageMapCacheLimit) return;

        foreach (var stale in PageMapCache
            .OrderBy(e => e.Value.CachedAt)
            .Take(Math.Max(1, PageMapCache.Count - PageMapCacheLimit + 1))
            .Select(e => e.Key)
            .ToList())
        {
            PageMapCache.TryRemove(stale, out _);
        }
    }

    private static readonly IHtmlParser HtmlParser = new HtmlParser();

    private static readonly string[] BlockSelectors =
        ["h1", "h2", "h3", "h4", "h5", "h6", "p", "blockquote", "li", "dd", "dt", "figcaption", "pre"];

    private static readonly string BlockSelector = string.Join(',', BlockSelectors);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
}
