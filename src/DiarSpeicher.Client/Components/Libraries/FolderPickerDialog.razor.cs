using DiarSpeicher.Client.Api;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DiarSpeicher.Client.Components.Libraries;

public sealed partial class FolderPickerDialog : IDisposable
{
    private const int MinSearchLength = 2;
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly DialogOptions PickerOptions = new() { MaxWidth = MaxWidth.Medium, FullWidth = true };
    private static readonly DialogOptions NewFolderOptions = new() { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };

    private List<FolderRootDto> _roots = [];
    private FolderListingDto? _listing;
    private string? _error;
    private bool _loading = true;
    private string _query = string.Empty;
    private List<FolderHit>? _hits;
    private bool _indexing;
    private bool _newFolderOpen;
    private string _newFolderName = string.Empty;
    private bool _creatingFolder;
    private string? _folderCreateError;
    private CancellationTokenSource? _search;
    private bool _wasVisible;

    [Parameter] public bool Visible { get; set; }

    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }

    [Parameter] public string? InitialPath { get; set; }

    [Parameter] public EventCallback<string> Selected { get; set; }

    [Inject] private FilesystemApi Filesystem { get; set; } = null!;

    private string Current => _listing?.Path ?? string.Empty;

    private bool CurrentIsRoot => Current.Length > 0 && IsRoot(Current);

    protected override async Task OnParametersSetAsync()
    {
        var opening = Visible && !_wasVisible;
        _wasVisible = Visible;
        if (!opening)
        {
            return;
        }

        _query = string.Empty;
        _hits = null;
        _error = null;
        _newFolderOpen = false;
        _listing = null;
        try
        {
            _roots = await Filesystem.RootsAsync() ?? [];
        }
        catch (ApiException)
        {
            _roots = [];
        }

        await BrowseAsync(string.IsNullOrWhiteSpace(InitialPath) ? null : InitialPath);
    }

    public void Dispose()
    {
        _search?.Cancel();
        _search?.Dispose();
    }

    private async Task BrowseAsync(string? path)
    {
        _loading = true;
        try
        {
            _listing = await Filesystem.BrowseAsync(path);
            _error = null;
        }
        catch (ApiException exception)
        {
            _listing = null;
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string Trimmed(string path) => path.Length > 1 ? path.TrimEnd('/') : path;

    private bool IsRoot(string path) => _roots.Exists(root => Trimmed(root.Path) == Trimmed(path));

    private async Task ChooseAsync(string path)
    {
        if (IsRoot(path))
        {
            return;
        }

        await Selected.InvokeAsync(path);
        await CloseAsync();
    }

    private Task CloseAsync() => VisibleChanged.InvokeAsync(false);

    private async Task OnQueryChanged(string value)
    {
        _query = value;
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
        var needle = value.Trim();
        if (needle.Length < MinSearchLength)
        {
            _hits = null;
            return;
        }

        _search = new CancellationTokenSource();
        var token = _search.Token;
        try
        {
            await Task.Delay(SearchDebounce, token);
            _hits = await Filesystem.SearchAsync(needle) ?? [];
        }
        catch (TaskCanceledException)
        {
        }
        catch (ApiException)
        {
            _hits = [];
        }
    }

    private async Task OpenHitAsync(FolderHit hit)
    {
        _query = string.Empty;
        _hits = null;
        await BrowseAsync(hit.Path);
    }

    private async Task RebuildIndexAsync()
    {
        _indexing = true;
        try
        {
            await Filesystem.RebuildIndexAsync();
        }
        catch (ApiException)
        {
        }
        finally
        {
            _indexing = false;
        }
    }

    private void OpenNewFolder()
    {
        _folderCreateError = null;
        _newFolderName = string.Empty;
        _newFolderOpen = true;
    }

    private async Task CreateFolderAsync()
    {
        var name = _newFolderName.Trim();
        if (name.Length == 0 || Current.Length == 0)
        {
            return;
        }

        _creatingFolder = true;
        _folderCreateError = null;
        try
        {
            var entry = await Filesystem.CreateFolderAsync(Current, name);
            _newFolderOpen = false;
            _newFolderName = string.Empty;
            await BrowseAsync(entry?.Path ?? Current);
        }
        catch (ApiException exception)
        {
            _folderCreateError = exception.Message;
        }
        finally
        {
            _creatingFolder = false;
        }
    }
}
