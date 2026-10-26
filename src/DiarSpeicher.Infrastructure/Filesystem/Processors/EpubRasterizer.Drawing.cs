using SkiaSharp;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public static partial class EpubRasterizer
{
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
