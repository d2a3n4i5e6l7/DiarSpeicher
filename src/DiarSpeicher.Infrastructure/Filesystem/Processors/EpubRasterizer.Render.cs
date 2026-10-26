using AngleSharp.Html.Parser;
using DiarSpeicher.Infrastructure.Filesystem.Imaging;
using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public static partial class EpubRasterizer
{
    /// <summary>
    /// Parsed as a DOM rather than with a regular expression: a regex pairs mismatched openings
    /// and closings, so a &lt;div&gt; closed by an &lt;/h1&gt; turns the chapter title into a
    /// paragraph, and wrapping content in div or section is the norm.
    /// </summary>
    private static List<RenderBlock> ExtractBlocks(string html)
    {
        using var doc = HtmlParser.ParseDocument(html);

        var bloques = new List<RenderBlock>();
        foreach (var el in doc.QuerySelectorAll(BlockSelector))
        {
            // A <p> inside a <li> is already contributed by the <p>: only leaves count.
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

    private static string CleanHtmlText(string raw)
    {
        var text = HtmlTagRegex.Replace(raw, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        return WhitespaceRegex.Replace(text, " ").Trim();
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

        using var webpData = SkiaImages.Encode(bitmap, SKEncodedImageFormat.Webp, 85);
        return webpData != null ? new ExtractedPage(ContentType.Webp, webpData.ToArray()) : null;
    }
}
