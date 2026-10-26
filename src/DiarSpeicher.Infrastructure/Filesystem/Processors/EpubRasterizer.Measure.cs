using System.Text.RegularExpressions;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public static partial class EpubRasterizer
{
    /// <summary>
    /// Returns (null, null) when the image cannot be measured: a corrupt file or a format Skia
    /// does not recognize must not break the whole page map of the book; the page simply falls
    /// back to the default sizing.
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
            var imagePage = await RenderImageOnlySubpageAsync(archive, entry, entryDir, ct);
            if (imagePage != null) return imagePage;
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

    private static async Task<ExtractedPage?> RenderImageOnlySubpageAsync(
        ZipArchive archive,
        ZipArchiveEntry entry,
        string entryDir,
        CancellationToken ct)
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

        return null;
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
}
