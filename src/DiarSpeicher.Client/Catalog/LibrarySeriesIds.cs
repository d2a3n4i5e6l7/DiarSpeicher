using DiarSpeicher.Client.Api;

namespace DiarSpeicher.Client.Catalog;

public static class LibrarySeriesIds
{
    public static async Task<HashSet<string>?> CollectAsync(SeriesApi series, string? libraryId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(libraryId))
        {
            return null;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var page = 0;
            var totalPages = 1;
            while (page < totalPages && !cancellationToken.IsCancellationRequested)
            {
                var result = await series.ListAsync(libraryId, page, MediaApi.MaxPageSize, cancellationToken);
                ids.UnionWith(result?.Data.Select(item => item.Id) ?? []);
                totalPages = result?.TotalPages ?? 0;
                page++;
            }
        }
        catch (ApiException)
        {
            return null;
        }

        return ids;
    }
}
