using System.Runtime.InteropServices.JavaScript;

namespace DiarSpeicher.Client.Interop;

internal static partial class ReaderBridgeJs
{
    public static event Action<bool>? FullscreenChanged;

    public static event Action? Scrolled;

    public static void Initialize()
    {
        AddDocumentListener("fullscreenchange", () => FullscreenChanged?.Invoke(IsFullscreen()), false);
        AddDocumentListener("scroll", () => Scrolled?.Invoke(), true);
    }

    public static async Task ToggleFullscreen()
    {
        try
        {
            await (IsFullscreen() ? ExitFullscreen() : RequestFullscreen());
        }
        catch (JSException)
        {
        }
    }

    public static double ImageRatio(string imageId)
    {
        using var image = DomJs.GetElementById(imageId);
        if (image is null)
        {
            return 0;
        }

        var width = image.GetPropertyAsDouble("naturalWidth");
        var height = image.GetPropertyAsDouble("naturalHeight");
        return width > 0 && height > 0 ? width / height : 0;
    }

    public static int? PageAtViewportCenter(string scrollerId, Func<int, string> slotId, int pageCount)
    {
        using var scroller = DomJs.GetElementById(scrollerId);
        if (scroller is null || pageCount < 1)
        {
            return null;
        }

        var center = scroller.GetPropertyAsDouble("scrollTop") + scroller.GetPropertyAsDouble("clientHeight") / 2;
        var (low, high) = (1, pageCount);
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            using var slot = DomJs.GetElementById(slotId(middle));
            if (slot is not null && slot.GetPropertyAsDouble("offsetTop") <= center)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static bool IsFullscreen()
    {
        using var document = JSHost.GlobalThis.GetPropertyAsJSObject("document");
        using var element = document?.GetPropertyAsJSObject("fullscreenElement");
        return element is not null;
    }

    [JSImport("globalThis.document.addEventListener")]
    private static partial void AddDocumentListener(string type, [JSMarshalAs<JSType.Function>] Action listener, bool capture);

    [JSImport("globalThis.document.exitFullscreen")]
    private static partial Task ExitFullscreen();

    [JSImport("globalThis.document.documentElement.requestFullscreen")]
    private static partial Task RequestFullscreen();
}
