using System.Runtime.InteropServices.JavaScript;

namespace DiarSpeicher.Client.Interop;

internal static partial class StorageJs
{
    public static string? GetItem(string key)
    {
        try
        {
            return LocalStorageGetItem(key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public static void SetItem(string key, string value) => IgnoreUnavailable(() => LocalStorageSetItem(key, value));

    public static void RemoveItem(string key) => IgnoreUnavailable(() => LocalStorageRemoveItem(key));

    private static void IgnoreUnavailable(Action write)
    {
        try
        {
            write();
        }
        catch (JSException)
        {
        }
    }

    [JSImport("globalThis.localStorage.getItem")]
    private static partial string? LocalStorageGetItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    private static partial void LocalStorageSetItem(string key, string value);

    [JSImport("globalThis.localStorage.removeItem")]
    private static partial void LocalStorageRemoveItem(string key);
}
