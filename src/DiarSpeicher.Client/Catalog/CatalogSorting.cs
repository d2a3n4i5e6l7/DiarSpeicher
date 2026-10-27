using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;

namespace DiarSpeicher.Client.Catalog;

public enum CatalogSortDirection
{
    Asc,
    Desc,
}

public sealed record SortOption(string Id, string Label, CatalogSortDirection DefaultDirection);

public static class CatalogSorting
{
    public static IReadOnlyList<SortOption> LibrarySorts { get; } =
    [
        new("name", "Nombre", CatalogSortDirection.Asc),
        new("createdAt", "Fecha de alta", CatalogSortDirection.Desc),
        new("lastScannedAt", "Último escaneo", CatalogSortDirection.Desc),
        new("mediaCount", "Nº de tomos", CatalogSortDirection.Desc),
        new("seriesCount", "Nº de series", CatalogSortDirection.Desc),
    ];

    public static IReadOnlyList<SortOption> SeriesSorts { get; } =
    [
        new("name", "Nombre", CatalogSortDirection.Asc),
        new("mediaCount", "Nº de tomos", CatalogSortDirection.Desc),
        new("status", "Estado", CatalogSortDirection.Asc),
    ];

    public static IReadOnlyList<SortOption> MediaSorts { get; } =
    [
        new("name", "Nombre", CatalogSortDirection.Asc),
        new("createdAt", "Fecha de subida", CatalogSortDirection.Desc),
        new("pages", "Nº de páginas", CatalogSortDirection.Desc),
        new("size", "Peso", CatalogSortDirection.Desc),
    ];

    public static List<DiarSpeicherLibraryDto> FilterAndSortLibraries(
        IEnumerable<DiarSpeicherLibraryDto> items, string query, string sort, CatalogSortDirection direction)
    {
        var needle = query.Trim();
        var filtered = needle.Length == 0
            ? items.ToList()
            : items.Where(item => Contains(item.Name, needle)).ToList();

        filtered.Sort((a, b) => sort switch
        {
            "createdAt" => CompareDates(a.CreatedAt, b.CreatedAt, direction),
            "lastScannedAt" => CompareDates(a.LastScannedAt, b.LastScannedAt, direction),
            "mediaCount" => Apply(a.MediaCount.CompareTo(b.MediaCount), direction),
            "seriesCount" => Apply(a.SeriesCount.CompareTo(b.SeriesCount), direction),
            _ => Apply(CompareText(a.Name, b.Name), direction),
        });
        return filtered;
    }

    public static List<DiarSpeicherSeriesDto> FilterAndSortSeries(
        IEnumerable<DiarSpeicherSeriesDto> items, string query, string sort, CatalogSortDirection direction)
    {
        var needle = query.Trim();
        var filtered = needle.Length == 0 ? items.ToList() : items.Where(item => Contains(item.Name, needle)).ToList();

        filtered.Sort((a, b) => sort switch
        {
            "mediaCount" => Apply(a.MediaCount.CompareTo(b.MediaCount), direction),
            "status" => Apply(CompareText(a.Status, b.Status), direction),
            _ => Apply(CompareText(a.Name, b.Name), direction),
        });
        return filtered;
    }

    public static List<DiarSpeicherMediaDto> FilterAndSortMedia(
        IEnumerable<DiarSpeicherMediaDto> items, string query, string sort, CatalogSortDirection direction)
    {
        var needle = query.Trim();
        var filtered = needle.Length == 0
            ? items.ToList()
            : items.Where(item => Contains(MediaHelpers.MediaTitle(item), needle)).ToList();

        filtered.Sort((a, b) => sort switch
        {
            "createdAt" => CompareDates(a.CreatedAt, b.CreatedAt, direction),
            "pages" => Apply(a.Pages.CompareTo(b.Pages), direction),
            "size" => Apply(a.Size.CompareTo(b.Size), direction),
            _ => Apply(CompareText(MediaHelpers.MediaTitle(a), MediaHelpers.MediaTitle(b)), direction),
        });
        return filtered;
    }

    public static int CompareText(string? a, string? b) => string.CompareOrdinal(SortKey.From(a), SortKey.From(b));

    private static bool Contains(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;

    private static int Apply(int result, CatalogSortDirection direction) => direction == CatalogSortDirection.Asc ? result : -result;

    private static int CompareDates(DateTimeOffset? a, DateTimeOffset? b, CatalogSortDirection direction) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => 1,
        (_, null) => -1,
        _ => Apply(a.Value.CompareTo(b.Value), direction),
    };
}
