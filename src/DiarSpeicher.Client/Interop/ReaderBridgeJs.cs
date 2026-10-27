using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace DiarSpeicher.Client.Interop;

internal static partial class ReaderBridgeJs
{
    private const string Module = "reader-bridge";

    public static event Action<int>? PageVisible;

    public static event Action<bool>? FullscreenChanged;

    [JSImport("toggleFullscreen", Module)]
    public static partial void ToggleFullscreen(string elementId);

    [JSImport("imageRatio", Module)]
    public static partial double ImageRatio(string imageId);

    [JSImport("observePages", Module)]
    public static partial void ObservePages(string containerId);

    [JSImport("disconnect", Module)]
    public static partial void Disconnect();

    [JSExport]
    internal static void OnPageVisible(int page) => PageVisible?.Invoke(page);

    [JSExport]
    internal static void OnFullscreenChanged(bool fullscreen) => FullscreenChanged?.Invoke(fullscreen);

    public static Task Load() => JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Source)));

    private const string Source = """"
        const VISIBLE_SHARE = 0.5;
        let observer = null;
        let exportsPromise = null;

        function exports() {
            exportsPromise ??= globalThis.getDotnetRuntime(0).getAssemblyExports("DiarSpeicher.Client.dll");
            return exportsPromise;
        }

        function bridge(handler) {
            exports().then((e) => handler(e.DiarSpeicher.Client.Interop.ReaderBridgeJs));
        }

        document.addEventListener("fullscreenchange", () => {
            const active = Boolean(document.fullscreenElement);
            bridge((b) => b.OnFullscreenChanged(active));
        });

        export function toggleFullscreen(elementId) {
            if (document.fullscreenElement) {
                document.exitFullscreen().catch(() => undefined);
                return;
            }
            document.getElementById(elementId)?.requestFullscreen().catch(() => undefined);
        }

        export function imageRatio(imageId) {
            const img = document.getElementById(imageId);
            if (!img || img.naturalWidth <= 0 || img.naturalHeight <= 0) {
                return 0;
            }
            return img.naturalWidth / img.naturalHeight;
        }

        export function observePages(containerId) {
            disconnect();
            const root = document.getElementById(containerId);
            if (!root) {
                return;
            }
            observer = new IntersectionObserver((entries) => {
                for (const entry of entries) {
                    if (entry.isIntersecting && entry.intersectionRatio > VISIBLE_SHARE) {
                        const value = Number(entry.target.dataset.page);
                        if (!Number.isNaN(value)) {
                            bridge((b) => b.OnPageVisible(value));
                        }
                    }
                }
            }, { threshold: [VISIBLE_SHARE] });
            for (const node of root.querySelectorAll("[data-page]")) {
                observer.observe(node);
            }
        }

        export function disconnect() {
            observer?.disconnect();
            observer = null;
        }
        """";
}
