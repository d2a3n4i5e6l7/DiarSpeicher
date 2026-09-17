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

public static class EpubRasterizer
{
    private static readonly ConcurrentDictionary<string, (EpubBookPageMap Map, DateTime CachedAt, DateTime FileWrittenAt)> PageMapCache = new();

    private const int PageMapCacheLimit = 256;

    private static readonly Regex ImgTagRegex = new(@"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SvgImageRegex = new(@"<image[^>]+(?:href|xlink:href)=[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

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
            // Sin fecha no se puede saber si el mapa cacheado sigue valiendo: MinValue nunca
            // casa con la comprobacion, asi que el mapa se reconstruye.
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

    /// <summary>
    /// Mide una imagen del EPUB y devuelve (null, null) si no se puede: un fichero corrupto o
    /// un formato que Skia no reconozca no debe tumbar el mapa entero del libro, la pagina
    /// simplemente cae al dimensionamiento por defecto.
    /// </summary>
    private static async Task<(int? Width, int? Height)> TryMeasureEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken ct)
    {
        try
        {
            await using var stream = await entry.OpenAsync(ct);
            return await PageMeasurer.MeasureAsync(stream, ct);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static async Task<EpubSubpageTarget?> TryBuildImageOnlyTargetAsync(
        ZipArchive archive,
        ZipArchiveEntry chapter,
        string html,
        List<RenderBlock> blocks,
        int chapterIndex,
        CancellationToken ct)
    {
        const int MaxTextLengthForImageOnly = 80;

        var imgSrc = FindMainImageSrc(html);
        if (string.IsNullOrEmpty(imgSrc) || blocks.Sum(b => b.Text.Length) >= MaxTextLengthForImageOnly)
        {
            return null;
        }

        var chapterDir = Path.GetDirectoryName(chapter.FullName)?.Replace('\\', '/') ?? "";
        var imgEntry = archive.GetEntry(ResolveZipPath(chapterDir, imgSrc));

        var (w, h) = imgEntry == null
            ? (null, null)
            : await TryMeasureEntryAsync(imgEntry, ct);

        return new EpubSubpageTarget(chapter.FullName, chapterIndex, 0, 1, IsImageOnly: true, w, h);
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
        return RenderSubpageToWebp(blocks, meta, profile, target.SubpageIndex, archive);
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

    private static readonly IHtmlParser HtmlParser = new HtmlParser();

    private static readonly string BlockSelector = string.Join(',', BlockSelectors);

    private static readonly string[] BlockSelectors =
        ["h1", "h2", "h3", "h4", "h5", "h6", "p", "blockquote", "li", "dd", "dt", "figcaption", "pre"];

    /// <summary>
    /// Los bloques de texto del capitulo, en orden y con su etiqueta real.
    ///
    /// Antes esto era una expresion regular que emparejaba aperturas y cierres distintos: un
    /// &lt;div&gt; cerraba con un &lt;/h1&gt; y el titulo del capitulo acababa clasificado como
    /// parrafo. Contra tres libros de tres editoriales se perdian todos o casi todos los
    /// titulos, porque envolver el contenido en div o section es lo normal.
    /// </summary>
    private static List<RenderBlock> ExtractBlocks(string html)
    {
        using var doc = HtmlParser.ParseDocument(html);

        var bloques = new List<RenderBlock>();
        foreach (var el in doc.QuerySelectorAll(BlockSelector))
        {
            // Un <p> dentro de un <li> ya lo aporta el <p>: solo cuentan las hojas.
            if (el.QuerySelector(BlockSelector) != null) continue;

            var texto = WhitespaceRegex.Replace(el.TextContent, " ").Trim();
            if (texto.Length == 0) continue;

            var nivel = el.TagName.Length == 2 && el.TagName[0] == 'H' && char.IsDigit(el.TagName[1])
                ? el.TagName[1] - '0'
                : 0;

            bloques.Add(new RenderBlock(texto, nivel > 0, nivel));
        }

        if (bloques.Count > 0) return bloques;

        var clean = CleanHtmlText(html);
        return clean
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new RenderBlock(line, false, 0))
            .ToList();
    }

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

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
        int subpageIndex,
        ZipArchive? archive = null)
    {
        var geometry = PageGeometry.For(profile);
        var width = geometry.Width;
        var marginX = geometry.MarginX;
        var marginY = geometry.MarginY;

        var colors = GetColors(profile.Theme);

        var fonts = EpubFontProvider.Resolve(profile, archive);
        var typeface = fonts.Regular;
        using var textFont = new SKFont(fonts.Regular, geometry.BaseFontSize) { Subpixel = true };
        using var headingFont = new SKFont(fonts.Bold, geometry.HeadingFontSize) { Subpixel = true };

        using var textPaint = new SKPaint { Color = colors.Text, IsAntialias = true };
        using var headingPaint = new SKPaint { Color = colors.Heading, IsAntialias = true };

        var metrics = geometry.ToMetrics();
        var (layoutItems, _) = MeasureLayout(blocks, textFont, headingFont, metrics);

        var contentTop = geometry.ContentTop;

        var finalHeight = profile.Height;
        var availableHeight = geometry.AvailableHeight;
        var subpageStart = subpageIndex * availableHeight;
        var subpageEnd = subpageStart + availableHeight;

        var pageItems = new List<(string text, float y, bool isHeading)>();
        foreach (var (text, y, isHeading) in layoutItems)
        {
            var relY = y - contentTop;
            if (relY >= subpageStart && relY < subpageEnd)
            {
                pageItems.Add((text, contentTop + (relY - subpageStart), isHeading));
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
        var currentY = metrics.MarginY + PageGeometry.HeaderHeight;
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
        var buffer = new char[text.Length + 1];
        var length = 0;

        foreach (var word in text.Split(' '))
        {
            var candidate = length == 0 ? word.Length : length + 1 + word.Length;

            if (length > 0) buffer[length] = ' ';
            word.CopyTo(0, buffer, candidate - word.Length, word.Length);

            if (font.MeasureText(buffer.AsSpan(0, candidate)) <= maxWidth)
            {
                length = candidate;
                continue;
            }

            if (length > 0) lines.Add(new string(buffer, 0, length));

            word.CopyTo(0, buffer, 0, word.Length);
            length = word.Length;
        }

        if (length > 0) lines.Add(new string(buffer, 0, length));

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

        using var headerPaint = new SKPaint { Color = colors.Accent, IsAntialias = true };
        using var headerFont = new SKFont(typeface, 20f);

        var titleStr = string.IsNullOrWhiteSpace(meta.BookTitle) ? "DIARSPEICHER ARCHIVE" : meta.BookTitle.ToUpperInvariant();
        if (titleStr.Length > 46) titleStr = string.Concat(titleStr.AsSpan(0, 43), "...");

        var headerY = marginY + 36f;
        canvas.DrawText(titleStr, marginX + 12f, headerY, SKTextAlign.Left, headerFont, headerPaint);

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

        using var logoPaint = new SKPaint { Color = colors.Muted.WithAlpha(180), IsAntialias = true };
        using var pagePaint = new SKPaint { Color = colors.Accent, IsAntialias = true };
        using var footerFont = new SKFont(typeface, 16f);

        canvas.DrawText("DIARSPEICHER", marginX + 8f, footerY, SKTextAlign.Left, footerFont, logoPaint);

        var pct = meta.TotalPages > 0 ? meta.PageNumber * 100f / meta.TotalPages : 0f;
        var rightLabel = $"{meta.PageNumber} / {meta.TotalPages}  ·  {pct:F0}%";
        var rightLen = footerFont.MeasureText(rightLabel);
        canvas.DrawText(rightLabel, width - marginX - rightLen - 8f, footerY, SKTextAlign.Left, footerFont, pagePaint);
    }
}
