using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace DiarSpeicher.Client.Interop;

internal static partial class DomJs
{
    private const string Module = "dom";

    [JSImport("scrollHorizontally", Module)]
    public static partial void ScrollHorizontally(string elementId, int delta);

    [JSImport("historyBack", Module)]
    public static partial bool HistoryBack();

    [JSImport("coverColumns", Module)]
    public static partial int CoverColumns(int minWidth, int gap);

    public static Task Load() => JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Source)));

    private const string Source = """"
        export function scrollHorizontally(elementId, delta) {
            document.getElementById(elementId)?.scrollBy({ left: delta, behavior: "smooth" });
        }

        export function historyBack() {
            if (globalThis.history.length <= 1) {
                return false;
            }
            globalThis.history.back();
            return true;
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
