using DiarSpeicher.Client.Interop;

namespace DiarSpeicher.Client.Catalog;

public sealed class LibraryFilterState
{
    private const string StorageKey = "diarspeicher-library-filter";

    public LibraryFilterState() => LibraryId = StorageJs.GetItem(StorageKey);

    public event Action? Changed;

    public string? LibraryId { get; private set; }

    public void Set(string? libraryId)
    {
        if (LibraryId == libraryId)
        {
            return;
        }

        LibraryId = libraryId;
        if (libraryId is null)
        {
            StorageJs.RemoveItem(StorageKey);
        }
        else
        {
            StorageJs.SetItem(StorageKey, libraryId);
        }

        Changed?.Invoke();
    }
}
