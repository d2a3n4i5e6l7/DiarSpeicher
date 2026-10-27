using DiarSpeicher.Core.Domain.Catalog;

namespace DiarSpeicher.Client.Catalog;

public static class MediaHelpers
{
    private const int Complete = 100;
    private const double PagesPerMinute = 1.5;

    public static int ReadProgress(DiarSpeicherMediaDto media)
    {
        if (media.IsCompleted)
        {
            return Complete;
        }

        if (media.ReadPercent is { } percent)
        {
            return Math.Min(Complete, (int)Math.Round(percent));
        }

        if (media.Pages <= 0 || media.CurrentPage is not > 0)
        {
            return 0;
        }

        return Math.Min(Complete, (int)Math.Round(media.CurrentPage.Value * (double)Complete / media.Pages));
    }

    public static string? ReadBadge(DiarSpeicherMediaDto media)
    {
        if (media.ReadPercent is { } percent)
        {
            return $"{(int)Math.Round(percent)}%";
        }

        return media.CurrentPage is > 0 ? $"P{media.CurrentPage}" : null;
    }

    public static string? RenderedLabel(DiarSpeicherMediaDto media)
    {
        if (media.RenderedPage is not { } page)
        {
            return null;
        }

        var finished = media.RenderedCompleted == true ? " · TERMINADO" : string.Empty;
        return $"MODO IMAGEN · PÁG {page} / {media.RenderedTotalPages ?? 0}{finished}";
    }

    public static int EstimatedMinutes(int pages) => Math.Max(1, (int)Math.Round(pages / PagesPerMinute));

    public static string MediaTitle(DiarSpeicherMediaDto media) =>
        string.IsNullOrWhiteSpace(media.Metadata?.Title) ? media.Name : media.Metadata.Title.Trim();

    public static string MediaSubtitle(DiarSpeicherMediaDto media) =>
        $"{media.Extension.TrimStart('.').ToUpperInvariant()} · {media.Pages} PÁGS";
}
