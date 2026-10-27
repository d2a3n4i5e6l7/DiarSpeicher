using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace DiarSpeicher.Client.Interop;

internal static partial class DomJs
{
    private const string Module = "dom";

    [JSImport("scrollHorizontally", Module)]
    public static partial void ScrollHorizontally(string elementId, int delta);

    [JSImport("globalThis.document.getElementById")]
    public static partial JSObject? GetElementById(string id);

    [JSImport("globalThis.history.back")]
    private static partial void GoBack();

    public static bool HistoryBack()
    {
        using var history = JSHost.GlobalThis.GetPropertyAsJSObject("history");
        if (history is null || history.GetPropertyAsInt32("length") <= 1)
        {
            return false;
        }

        GoBack();
        return true;
    }

    [JSImport("coverColumns", Module)]
    public static partial int CoverColumns(int minWidth, int gap);

    public static Task Load() => JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Source)));

    private const string Source = """"
        export function scrollHorizontally(elementId, delta) {
            document.getElementById(elementId)?.scrollBy({ left: delta, behavior: "smooth" });
        }

        export function coverColumns(minWidth, gap) {
            const host = document.querySelector(".ds-main-scroll");
            if (!host) {
                return 0;
            }
            const style = getComputedStyle(host);
            const width = host.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
            return Math.max(1, Math.floor((width + gap) / (minWidth + gap)));
        }
        """";
}
