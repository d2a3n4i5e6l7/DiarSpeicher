namespace DiarSpeicher.Infrastructure.Reading;

public static class ReadingProgressExtensions
{
    public static async Task<IReadOnlyDictionary<string, int>?> EpubTotalsAsync(
        this IReadingProgress progress,
        IEnumerable<Media> books,
        CancellationToken ct = default)
    {
        var epubIds = books.Where(EpubPageMapStore.IsEpub).Select(b => b.Id).ToList();
        return epubIds.Count == 0 ? null : await progress.GetStoredTotalsAsync(epubIds, ct);
    }

    public static int? TotalFor(this IReadOnlyDictionary<string, int>? totals, string mediaId) =>
        totals is not null && totals.TryGetValue(mediaId, out var total) && total > 0 ? total : null;
}
