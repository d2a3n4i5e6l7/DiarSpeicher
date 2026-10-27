using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace DiarSpeicher.Client.Interop;

internal static partial class StorageJs
{
    private const string Module = "storage";

    [JSImport("getItem", Module)]
    public static partial string? GetItem(string key);

    [JSImport("setItem", Module)]
    public static partial void SetItem(string key, string value);

    [JSImport("removeItem", Module)]
    public static partial void RemoveItem(string key);

    public static Task Load() => JSHost.ImportAsync(Module, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Source)));

    private const string Source = """"
        export function getItem(key) {
            try {
                return globalThis.localStorage.getItem(key);
            } catch {
                return null;
            }
        }

        export function setItem(key, value) {
            try {
                globalThis.localStorage.setItem(key, value);
            } catch {
            }
        }

        export function removeItem(key) {
            try {
                globalThis.localStorage.removeItem(key);
            } catch {
            }
        }
        """";
}
