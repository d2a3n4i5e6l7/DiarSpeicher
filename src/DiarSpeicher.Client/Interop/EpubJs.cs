using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace DiarSpeicher.Client.Interop;

internal static partial class EpubJs
{
    private const string Module = "epub-bridge";

    public static event Action<double, double>? DownloadProgress;

    public static event Action<int, bool>? Relocated;

    public static event Action<string>? TocLoaded;

    public static event Action? Clicked;

    [JSImport("open", Module)]
    public static partial Task Open(string containerId, string fileUrl, string? dpopProof, string theme);

    [JSImport("next", Module)]
    public static partial void Next();

    [JSImport("prev", Module)]
    public static partial void Prev();

    [JSImport("fontSize", Module)]
    public static partial void FontSize(int percent);

    [JSImport("selectTheme", Module)]
    public static partial void SelectTheme(string theme);

    [JSImport("display", Module)]
    public static partial void Display(string href);

    [JSImport("destroy", Module)]
    public static partial void Destroy();

    [JSExport]
    internal static void OnDownloadProgress(double loaded, double total) => DownloadProgress?.Invoke(loaded, total);

    [JSExport]
    internal static void OnRelocated(int percentage, bool completed) => Relocated?.Invoke(percentage, completed);

    [JSExport]
    internal static void OnToc(string json) => TocLoaded?.Invoke(json);

    [JSExport]
    internal static void OnClick() => Clicked?.Invoke();

    public static Task Load() => JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Source)));

    private const string Source = """"
        const LIBS = ["lib/epubjs/jszip-3.10.2.min.js", "lib/epubjs/epub-0.3.93.min.js"];
        const PERCENT = 100;
        const NEARLY_DONE = 99;

        const THEMES = {
            ironBlood: {
                body: { background: "#050508 !important", color: "#F0F2F6 !important", "font-family": "'Inter', sans-serif !important", "line-height": "1.75 !important", padding: "0 24px !important" },
                p: { color: "#F0F2F6 !important" },
                "h1, h2, h3, h4": { color: "#FF2E2E !important", "font-family": "'Rajdhani', sans-serif !important" },
                a: { color: "#FF2E2E !important" },
            },
            pureBlack: {
                body: { background: "#000000 !important", color: "#D1D5DB !important", "font-family": "'Inter', sans-serif !important", "line-height": "1.75 !important", padding: "0 24px !important" },
                p: { color: "#D1D5DB !important" },
                "h1, h2, h3, h4": { color: "#FFFFFF !important" },
                a: { color: "#9CA3AF !important" },
            },
            sepia: {
                body: { background: "#1C1814 !important", color: "#E2D9CE !important", "font-family": "'Inter', sans-serif !important", "line-height": "1.75 !important", padding: "0 24px !important" },
                p: { color: "#E2D9CE !important" },
                "h1, h2, h3, h4": { color: "#E8A87C !important" },
                a: { color: "#E8A87C !important" },
            },
        };

        let book = null;
        let rendition = null;
        let libsPromise = null;
        let exportsPromise = null;

        function bridge(handler) {
            exportsPromise ??= globalThis.getDotnetRuntime(0).getAssemblyExports("DiarSpeicher.Client.dll");
            exportsPromise.then((e) => handler(e.DiarSpeicher.Client.Interop.EpubJs));
        }

        function loadScript(src) {
            return new Promise((resolve, reject) => {
                const script = document.createElement("script");
                script.src = src;
                script.onload = () => resolve();
                script.onerror = () => reject(new Error(`No se pudo cargar ${src}`));
                document.head.appendChild(script);
            });
        }

        function ensureLibs() {
            libsPromise ??= LIBS.reduce((chain, src) => chain.then(() => loadScript(src)), Promise.resolve());
            return libsPromise;
        }

        async function download(fileUrl, dpopProof) {
            const headers = dpopProof ? { DPoP: dpopProof } : {};
            const response = await fetch(fileUrl, { credentials: "include", headers });
            if (!response.ok) {
                throw new Error(`${response.status} ${response.statusText}`);
            }
            const total = Number(response.headers.get("content-length")) || 0;
            if (!total || !response.body) {
                return await response.arrayBuffer();
            }
            const reader = response.body.getReader();
            const chunks = [];
            let loaded = 0;
            for (;;) {
                const { done, value } = await reader.read();
                if (done) break;
                chunks.push(value);
                loaded += value.length;
                bridge((b) => b.OnDownloadProgress(loaded, total));
            }
            const all = new Uint8Array(loaded);
            let position = 0;
            for (const chunk of chunks) {
                all.set(chunk, position);
                position += chunk.length;
            }
            return all.buffer;
        }

        function bookFraction(start, spineLength) {
            if (typeof start.percentage === "number") return start.percentage;
            const { displayed, index } = start;
            if (!displayed || displayed.total <= 0 || typeof index !== "number" || spineLength <= 0) return null;
            return (index + displayed.page / displayed.total) / spineLength;
        }

        function onRelocated(location) {
            if (!location?.start) return;
            const fraction = bookFraction(location.start, book.spine.length);
            if (fraction === null) return;
            const percentage = Math.min(PERCENT, Math.max(1, Math.round(fraction * PERCENT)));
            const completed = location.atEnd === true || percentage >= NEARLY_DONE;
            bridge((b) => b.OnRelocated(percentage, completed));
        }

        export async function open(containerId, fileUrl, dpopProof, theme) {
            await ensureLibs();
            const buffer = await download(fileUrl, dpopProof);
            const container = document.getElementById(containerId);
            book = globalThis.ePub(buffer);
            rendition = book.renderTo(container, { width: "100%", height: "100%", flow: "paginated", spread: "none" });
            for (const [name, styles] of Object.entries(THEMES)) {
                rendition.themes.register(name, styles);
            }
            rendition.themes.select(theme);
            await rendition.display();
            book.loaded.navigation
                .then((nav) => {
                    const toc = (nav?.toc ?? []).map((item) => ({ label: item.label?.trim() || "Capítulo", href: item.href }));
                    bridge((b) => b.OnToc(JSON.stringify(toc)));
                })
                .catch(() => undefined);
            rendition.on("relocated", onRelocated);
            rendition.on("click", () => bridge((b) => b.OnClick()));
        }

        export function next() { rendition?.next(); }
        export function prev() { rendition?.prev(); }
        export function fontSize(percent) { rendition?.themes.fontSize(`${percent}%`); }
        export function selectTheme(theme) { rendition?.themes.select(theme); }
        export function display(href) { rendition?.display(href); }

        export function destroy() {
            try {
                rendition?.destroy();
                book?.destroy();
            } catch {
            }
            rendition = null;
            book = null;
        }
        """";
}
