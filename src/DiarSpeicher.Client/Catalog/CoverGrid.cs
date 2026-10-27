using DiarSpeicher.Client.Interop;

namespace DiarSpeicher.Client.Catalog;

internal static class CoverGrid
{
    private const int MinCardWidth = 170;
    private const int Gap = 20;
    private const int TargetPageSize = 24;

    public static int PageSize()
    {
        var columns = DomJs.CoverColumns(MinCardWidth, Gap);
        return columns <= 0 ? TargetPageSize : columns * (int)Math.Ceiling(TargetPageSize / (double)columns);
    }
}
