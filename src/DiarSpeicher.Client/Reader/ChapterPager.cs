using System.Runtime.InteropServices.JavaScript;
using DiarSpeicher.Client.Interop;

namespace DiarSpeicher.Client.Reader;

internal sealed class ChapterPager(string elementId)
{
    public int Page { get; private set; }

    public int Count { get; private set; } = 1;

    public double StartFraction => Page / (double)Count;

    public bool IsLast => Page >= Count - 1;

    public void Measure()
    {
        using var element = DomJs.GetElementById(elementId);
        if (element is not null && MeasureStep(element) is { } step)
        {
            Page = Math.Clamp((int)Math.Round(element.GetPropertyAsDouble("scrollLeft") / step), 0, Count - 1);
        }
    }

    public void Show(int page)
    {
        using var element = DomJs.GetElementById(elementId);
        if (element is null || MeasureStep(element) is not { } step)
        {
            return;
        }

        Page = Math.Clamp(page, 0, Count - 1);
        element.SetProperty("scrollLeft", Page * step);
    }

    public void ShowLast() => Show(int.MaxValue);

    public void ShowFraction(double fraction)
    {
        Measure();
        Show((int)Math.Floor(fraction * Count));
    }

    public void ShowAnchor(string anchorId)
    {
        using var element = DomJs.GetElementById(elementId);
        using var anchor = DomJs.GetElementById(anchorId);
        if (element is null || anchor is null || MeasureStep(element) is not { } step)
        {
            Show(0);
            return;
        }

        Show((int)Math.Floor(anchor.GetPropertyAsDouble("offsetLeft") / step));
    }

    private double? MeasureStep(JSObject element)
    {
        var width = element.GetPropertyAsDouble("clientWidth");
        if (width <= 0)
        {
            return null;
        }

        var scrollWidth = element.GetPropertyAsDouble("scrollWidth");
        Count = Math.Max(1, (int)Math.Round(scrollWidth / width));
        return scrollWidth / Count;
    }
}
