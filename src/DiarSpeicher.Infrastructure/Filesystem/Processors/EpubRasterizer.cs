using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
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

public static class EpubRasterizer
{
    private static readonly ConcurrentDictionary<string, (EpubBookPageMap Map, DateTime CachedAt)> PageMapCache = new();

    private static readonly Regex ImgTagRegex = new(@"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SvgImageRegex = new(@"<image[^>]+(?:href|xlink:href)=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(@"<(?:p|h[1-6]|div|blockquote)[^>]*>(.*?)</(?:p|h[1-6]|div|blockquote)>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    public record RenderBlock(string Text, bool IsHeading, int HeadingLevel);

    public record ThemeColors(SKColor Bg, SKColor Text, SKColor Heading, SKColor Muted, SKColor Accent);

    public record PageMeta(int PageNumber, int TotalPages, string? BookTitle, int ChapterIndex = 0, int SubpageIndex = 0, int TotalSubpages = 1);

    public record LayoutMetrics(float ContentWidth, float LineHeight, float HeadingLineHeight, float ParagraphSpacing, float MarginY);

    public static async Task<EpubBookPageMap> GetOrBuildPageMapAsync(
        ZipArchive archive,
        string epubPath,
        List<ZipArchiveEntry> spineEntries,
        ZipArchiveEntry? coverEntry,
        EpubDeviceProfile profile,
        CancellationToken ct = default)
    {
        var cacheKey = $"{epubPath}:{profile.Width}x{profile.Height}:{profile.FontSize}:{profile.LineHeight}:{profile.MarginHorizontal}:{profile.MarginVertical}:{profile.AutoHeight}:{profile.FontFamily}:{profile.Theme}";
        if (PageMapCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 60)
        {
            return cached.Map;
        }

        var width = Math.Max(480, profile.Width);
        var marginX = Math.Max(24, profile.MarginHorizontal);
        var marginY = Math.Max(40, profile.MarginVertical);
        var contentWidth = width - (marginX * 2);

        var baseFontSize = Math.Max(16, profile.FontSize);
        var lineHeight = baseFontSize * Math.Max(1.2f, profile.LineHeight);
        var headingFontSize = baseFontSize * 1.35f;
        var headingLineHeight = headingFontSize * 1.3f;
        var paragraphSpacing = baseFontSize * 0.75f;

        using var typeface = SKTypeface.FromFamilyName(profile.FontFamily) ?? SKTypeface.Default;
        using var textFont = new SKFont(typeface, baseFontSize) { Subpixel = true };
        using var headingTypeface = SKTypeface.FromFamilyName(profile.FontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? typeface;
        using var headingFont = new SKFont(headingTypeface, headingFontSize) { Subpixel = true };

        var metrics = new LayoutMetrics(contentWidth, lineHeight, headingLineHeight, paragraphSpacing, marginY);

        const float headerHeight = 110f;
        const float footerHeight = 90f;
        var availableHeight = Math.Max(300f, profile.Height - (marginY * 2) - headerHeight - footerHeight);

        var targets = new List<EpubSubpageTarget>();

        if (coverEntry != null && !spineEntries.Any(s => string.Equals(s.FullName, coverEntry.FullName, StringComparison.OrdinalIgnoreCase)))
        {
            int? w = null, h = null;
            try
            {
                await using var s = await coverEntry.OpenAsync(ct);
                (w, h) = await PageMeasurer.MeasureAsync(s, ct);
            }
            catch (Exception)
            {
                // Ignorado: si falla la medicion se usa el dimensionamiento por defecto
            }
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

            var imgSrc = FindMainImageSrc(html);
            var blocks = ExtractBlocks(html);
            var totalTextLen = blocks.Sum(b => b.Text.Length);

            if (!string.IsNullOrEmpty(imgSrc) && totalTextLen < 80)
            {
                int? w = null, h = null;
                try
                {
                    var chapterDir = Path.GetDirectoryName(chapter.FullName)?.Replace('\\', '/') ?? "";
                    var resolved = ResolveZipPath(chapterDir, imgSrc);
                    var imgEntry = archive.GetEntry(resolved);
                    if (imgEntry != null)
                    {
                        await using var s = await imgEntry.OpenAsync(ct);
                        (w, h) = await PageMeasurer.MeasureAsync(s, ct);
                    }
                }
                catch (Exception)
                {
                    // Ignorado: si falla la medicion se usa el dimensionamiento por defecto
                }
                targets.Add(new EpubSubpageTarget(chapter.FullName, i + 1, 0, 1, IsImageOnly: true, w, h));
                continue;
            }

            if (profile.AutoHeight)
            {
                targets.Add(new EpubSubpageTarget(chapter.FullName, i + 1, 0, 1, IsImageOnly: false));
            }
            else
            {
                var (_, totalContentHeight) = MeasureLayout(blocks, textFont, headingFont, metrics);
                var usableContentHeight = Math.Max(1f, totalContentHeight - headerHeight - marginY);
                var subpageCount = Math.Max(1, (int)Math.Ceiling(usableContentHeight / availableHeight));
                for (var p = 0; p < subpageCount; p++)
                {
                    targets.Add(new EpubSubpageTarget(chapter.FullName, i + 1, p, subpageCount, IsImageOnly: false));
                }
            }
        }

        var map = new EpubBookPageMap { BookPath = epubPath, Pages = targets };
        PageMapCache[cacheKey] = (map, DateTime.UtcNow);
        return map;
    }

    public static async Task<ExtractedPage?> RenderSubpageAsync(
        ZipArchive archive,
        EpubSubpageTarget target,
        int globalPageNumber,
        int totalBookPages,
        string? bookTitle,
        EpubDeviceProfile profile,
        CancellationToken ct = default)
    {
        var entry = archive.GetEntry(target.EntryFullName);
        if (entry == null) return null;

        var entryDir = Path.GetDirectoryName(entry.FullName)?.Replace('\\', '/') ?? "";

        if (target.IsImageOnly)
        {
            string htmlContent;
            await using (var stream = await entry.OpenAsync(ct))
            using (var reader = new StreamReader(stream))
            {
                htmlContent = await reader.ReadToEndAsync(ct);
            }

            var imgSrc = FindMainImageSrc(htmlContent);
            if (!string.IsNullOrEmpty(imgSrc))
            {
                var resolvedPath = ResolveZipPath(entryDir, imgSrc);
                var imgEntry = archive.GetEntry(resolvedPath);
                if (imgEntry != null)
                {
                    await using var imgStream = await imgEntry.OpenAsync(ct);
                    using var ms = new MemoryStream();
                    await imgStream.CopyToAsync(ms, ct);
                    var ctType = ContentTypeExtensions.FromExtension(Path.GetExtension(imgEntry.FullName));
                    return new ExtractedPage(ctType, ms.ToArray());
                }
            }

            var directExt = Path.GetExtension(entry.FullName).ToLowerInvariant();
            if (directExt is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                await using var imgStream = await entry.OpenAsync(ct);
                using var ms = new MemoryStream();
                await imgStream.CopyToAsync(ms, ct);
                return new ExtractedPage(ContentTypeExtensions.FromExtension(directExt), ms.ToArray());
            }
        }

        string textHtml;
        await using (var stream = await entry.OpenAsync(ct))
        using (var reader = new StreamReader(stream))
        {
            textHtml = await reader.ReadToEndAsync(ct);
        }

        var blocks = ExtractBlocks(textHtml);
        var meta = new PageMeta(globalPageNumber, totalBookPages, bookTitle, target.ChapterSpineIndex, target.SubpageIndex, target.TotalSubpagesInChapter);
        return RenderSubpageToWebp(blocks, meta, profile, target.SubpageIndex);
    }

    private static string? FindMainImageSrc(string html)
    {
        var m = ImgTagRegex.Match(html);
        if (m.Success) return m.Groups[1].Value;

        var mSvg = SvgImageRegex.Match(html);
        return mSvg.Success ? mSvg.Groups[1].Value : null;
    }

    private static string ResolveZipPath(string baseDir, string relativePath)
    {
        var cleanRel = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(baseDir)) return cleanRel;

        var combined = $"{baseDir}/{cleanRel}";
        var parts = combined.Split('/');
        var stack = new List<string>();
        foreach (var p in parts)
        {
            if (p == "." || string.IsNullOrEmpty(p)) continue;
            if (p == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(p);
            }
        }
        return string.Join('/', stack);
    }

    private static List<RenderBlock> ExtractBlocks(string html)
    {
        var matches = ParagraphRegex.Matches(html);
        if (matches.Count > 0)
        {
            return matches
                .Select(m => ParseBlock(m.Value, m.Groups[1].Value))
                .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                .ToList();
        }

        var clean = CleanHtmlText(html);
        var lines = clean.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new RenderBlock(line, false, 0))
            .ToList();
    }

    private static RenderBlock ParseBlock(string tagBlock, string innerHtml)
    {
        var text = CleanHtmlText(innerHtml);
        var isHeading = tagBlock.StartsWith("<h", StringComparison.OrdinalIgnoreCase);
        var level = isHeading && char.IsDigit(tagBlock[2]) ? (int)char.GetNumericValue(tagBlock[2]) : 0;
        return new RenderBlock(text, isHeading, level);
    }

    private static string CleanHtmlText(string raw)
    {
        var text = HtmlTagRegex.Replace(raw, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static ThemeColors GetColors(string theme) => theme.ToLowerInvariant() switch
    {
        "light" or "white" or "eink" => new ThemeColors(
            SKColor.Parse("#FFFFFF"),
            SKColor.Parse("#111111"),
            SKColor.Parse("#000000"),
            SKColor.Parse("#555555"),
            SKColor.Parse("#C21818")),
        "oled" => new ThemeColors(
            SKColor.Parse("#000000"),
            SKColor.Parse("#FFFFFF"),
            SKColor.Parse("#FF3E3E"),
            SKColor.Parse("#757575"),
            SKColor.Parse("#C21818")),
        "sepia" => new ThemeColors(
            SKColor.Parse("#1C1814"),
            SKColor.Parse("#EAD9C2"),
            SKColor.Parse("#E29D62"),
            SKColor.Parse("#968270"),
            SKColor.Parse("#B84224")),
        _ => new ThemeColors(
            SKColor.Parse("#050508"),
            SKColor.Parse("#F0F2F6"),
            SKColor.Parse("#FF2E2E"),
            SKColor.Parse("#8E95A5"),
            SKColor.Parse("#C21818"))
    };

    private static ExtractedPage? RenderSubpageToWebp(
        List<RenderBlock> blocks,
        PageMeta meta,
        EpubDeviceProfile profile,
        int subpageIndex)
    {
        var width = Math.Max(480, profile.Width);
        var marginX = Math.Max(24, profile.MarginHorizontal);
        var marginY = Math.Max(40, profile.MarginVertical);
        var contentWidth = width - (marginX * 2);

        var colors = GetColors(profile.Theme);
        var baseFontSize = Math.Max(16, profile.FontSize);
        var lineHeight = baseFontSize * Math.Max(1.2f, profile.LineHeight);
        var headingFontSize = baseFontSize * 1.35f;
        var headingLineHeight = headingFontSize * 1.3f;
        var paragraphSpacing = baseFontSize * 0.75f;

        using var typeface = SKTypeface.FromFamilyName(profile.FontFamily) ?? SKTypeface.Default;
        using var textFont = new SKFont(typeface, baseFontSize) { Subpixel = true };
        using var headingTypeface = SKTypeface.FromFamilyName(profile.FontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? typeface;
        using var headingFont = new SKFont(headingTypeface, headingFontSize) { Subpixel = true };

        using var textPaint = new SKPaint { Color = colors.Text, IsAntialias = true };
        using var headingPaint = new SKPaint { Color = colors.Heading, IsAntialias = true };

        var metrics = new LayoutMetrics(contentWidth, lineHeight, headingLineHeight, paragraphSpacing, marginY);
        var (layoutItems, totalContentHeight) = MeasureLayout(blocks, textFont, headingFont, metrics);

        const float headerHeight = 110f;
        const float footerHeight = 90f;
        var contentTop = marginY + headerHeight;

        int finalHeight;
        List<(string text, float y, bool isHeading)> pageItems;

        if (profile.AutoHeight)
        {
            var totalRequiredHeight = totalContentHeight + footerHeight + marginY;
            finalHeight = (int)Math.Max(profile.Height, totalRequiredHeight);
            pageItems = layoutItems;
        }
        else
        {
            finalHeight = profile.Height;
            var availableHeight = Math.Max(300f, profile.Height - (marginY * 2) - headerHeight - footerHeight);
            var subpageStart = subpageIndex * availableHeight;
            var subpageEnd = subpageStart + availableHeight;

            pageItems = new List<(string text, float y, bool isHeading)>();
            foreach (var (text, y, isHeading) in layoutItems)
            {
                var relY = y - contentTop;
                if (relY >= subpageStart && relY < subpageEnd)
                {
                    pageItems.Add((text, contentTop + (relY - subpageStart), isHeading));
                }
            }
        }

        using var bitmap = new SKBitmap(width, finalHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(colors.Bg);

        DrawTacticalHeader(canvas, width, marginX, marginY, meta, colors, typeface);

        foreach (var (text, y, isHeading) in pageItems)
        {
            canvas.DrawText(text, marginX, y, SKTextAlign.Left, isHeading ? headingFont : textFont, isHeading ? headingPaint : textPaint);
        }

        DrawTacticalFooter(canvas, width, finalHeight, marginX, meta, colors, typeface);

        using var image = SKImage.FromBitmap(bitmap);
        using var webpData = image.Encode(SKEncodedImageFormat.Webp, 85);
        return webpData != null ? new ExtractedPage(ContentType.Webp, webpData.ToArray()) : null;
    }

    private static (List<(string text, float y, bool isHeading)> items, float totalHeight) MeasureLayout(
        List<RenderBlock> blocks,
        SKFont textFont,
        SKFont headingFont,
        LayoutMetrics metrics)
    {
        const float headerHeight = 110f;
        var currentY = metrics.MarginY + headerHeight;
        var items = new List<(string text, float y, bool isHeading)>();

        foreach (var block in blocks)
        {
            var font = block.IsHeading ? headingFont : textFont;
            var currentLineHeight = block.IsHeading ? metrics.HeadingLineHeight : metrics.LineHeight;
            var wrappedLines = WrapText(block.Text, font, metrics.ContentWidth);

            if (block.IsHeading)
            {
                currentY += metrics.ParagraphSpacing * 0.5f;
            }

            foreach (var line in wrappedLines)
            {
                currentY += currentLineHeight;
                items.Add((line, currentY, block.IsHeading));
            }

            currentY += metrics.ParagraphSpacing;
        }

        return (items, currentY);
    }

    private static List<string> WrapText(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        var words = text.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
            var measured = font.MeasureText(testLine);
            if (measured <= maxWidth)
            {
                currentLine = testLine;
            }
            else
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                }
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return lines;
    }

    private static void DrawTacticalHeader(
        SKCanvas canvas,
        int width,
        float marginX,
        float marginY,
        PageMeta meta,
        ThemeColors colors,
        SKTypeface typeface)
    {
        using var accentPaint = new SKPaint { Color = colors.Accent, StrokeWidth = 2.5f, IsStroke = true, IsAntialias = true };

        const float bracketSize = 16f;
        canvas.DrawLine(marginX, marginY, marginX + bracketSize, marginY, accentPaint);
        canvas.DrawLine(marginX, marginY, marginX, marginY + bracketSize, accentPaint);
        canvas.DrawLine(width - marginX, marginY, width - marginX - bracketSize, marginY, accentPaint);
        canvas.DrawLine(width - marginX, marginY, width - marginX, marginY + bracketSize, accentPaint);

        using var headerPaint = new SKPaint { Color = colors.Muted, IsAntialias = true };
        using var headerFont = new SKFont(typeface, 20f);

        var titleStr = string.IsNullOrWhiteSpace(meta.BookTitle) ? "DIARSPEICHER ARCHIVE" : meta.BookTitle.ToUpperInvariant();
        if (titleStr.Length > 36) titleStr = string.Concat(titleStr.AsSpan(0, 33), "...");

        var headerY = marginY + 36f;
        canvas.DrawText(titleStr, marginX + 12f, headerY, SKTextAlign.Left, headerFont, headerPaint);

        string pageIndicator;
        if (meta.TotalSubpages > 1)
        {
            pageIndicator = $"CH. {meta.ChapterIndex:D2} · PÁG. {meta.SubpageIndex + 1}/{meta.TotalSubpages}";
        }
        else if (meta.ChapterIndex > 0)
        {
            pageIndicator = $"CH. {meta.ChapterIndex:D2}";
        }
        else
        {
            pageIndicator = "";
        }

        if (!string.IsNullOrEmpty(pageIndicator))
        {
            var pageLen = headerFont.MeasureText(pageIndicator);
            canvas.DrawText(pageIndicator, width - marginX - pageLen - 12f, headerY, SKTextAlign.Left, headerFont, headerPaint);
        }

        using var barPaint = new SKPaint { Color = colors.Accent.WithAlpha(110), StrokeWidth = 1f, IsStroke = true, IsAntialias = true };
        canvas.DrawLine(marginX, marginY + 54f, width - marginX, marginY + 54f, barPaint);
    }

    private static void DrawTacticalFooter(
        SKCanvas canvas,
        int width,
        int finalHeight,
        float marginX,
        PageMeta meta,
        ThemeColors colors,
        SKTypeface typeface)
    {
        var footerY = finalHeight - 40f;

        using var barPaint = new SKPaint { Color = colors.Accent.WithAlpha(90), StrokeWidth = 1f, IsStroke = true, IsAntialias = true };
        canvas.DrawLine(marginX, footerY - 24f, width - marginX, footerY - 24f, barPaint);

        using var footerPaint = new SKPaint { Color = colors.Muted.WithAlpha(180), IsAntialias = true };
        using var footerFont = new SKFont(typeface, 16f);

        canvas.DrawText("DIARSPEICHER // PROTOCOL ARCHIVE", marginX + 8f, footerY, SKTextAlign.Left, footerFont, footerPaint);

        var rightLabel = $"[ PÁG. {meta.PageNumber:D3} // {meta.TotalPages:D3} ]";
        var rightLen = footerFont.MeasureText(rightLabel);
        canvas.DrawText(rightLabel, width - marginX - rightLen - 8f, footerY, SKTextAlign.Left, footerFont, footerPaint);
    }
}
