using SharpCompress.Archives;

namespace DiarSpeicher.Infrastructure.Filesystem.Processors;

public readonly record struct ArchiveEntryRef(
    string Name,
    long Size,
    Func<CancellationToken, Task<Stream>> OpenAsync)
{
    public static ArchiveEntryRef From(ZipArchiveEntry entry) =>
        new(entry.FullName, entry.Length, async ct => await entry.OpenAsync(ct));

    public static ArchiveEntryRef From(IArchiveEntry entry) =>
        new(entry.Key ?? string.Empty, entry.Size, async ct => await entry.OpenEntryStreamAsync(ct));

    public static List<ArchiveEntryRef> From(IEnumerable<ZipArchiveEntry> entries) =>
        entries.Select(From).ToList();

    public static List<ArchiveEntryRef> From(IEnumerable<IArchiveEntry> entries) =>
        entries.Select(From).ToList();
}

public static class ArchiveEntryReader
{
    public static async Task<ExtractedPage> ReadAsync(ArchiveEntryRef entry, CancellationToken ct)
    {
        await using var stream = await entry.OpenAsync(ct);
        using var ms = new MemoryStream(entry.Size is > 0 and < int.MaxValue ? (int)entry.Size : 0);
        await stream.CopyToAsync(ms, ct);

        var contentType = ContentTypeExtensions.FromExtension(Path.GetExtension(entry.Name));
        return new ExtractedPage(contentType, ms.ToArray());
    }

    public static async Task<List<MeasuredPage>> MeasurePagesAsync(
        List<ArchiveEntryRef> imageEntries,
        CancellationToken ct)
    {
        var dimensions = new List<MeasuredPage>(imageEntries.Count);
        for (var i = 0; i < imageEntries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var pageEntry = imageEntries[i];
            int? width = null;
            int? height = null;

            try
            {
                await using var headerStream = await pageEntry.OpenAsync(ct);
                (width, height) = await PageMeasurer.MeasureAsync(headerStream, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una pagina ilegible se guarda sin dimensiones y el resto sigue.
            }

            dimensions.Add(new MeasuredPage
            {
                Number = i + 1,
                FileName = Path.GetFileName(pageEntry.Name),
                MediaType = PageMeasurer.MimeTypeFor(pageEntry.Name),
                Width = width,
                Height = height,
                SizeBytes = pageEntry.Size
            });
        }

        return dimensions;
    }
}
