using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Upload;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace DiarSpeicher.Client.Components.Libraries;

public sealed partial class UploadPanel : IDisposable
{
    private const int MaxFilesPerDrop = 1000;
    private const long TightDiskBytes = 5L * 1024 * 1024 * 1024;
    private const int PercentScale = 100;

    private static readonly string Accept = string.Join(',', PathUtils.AcceptedMediaExtensions.Select(extension => $".{extension}"));

    private readonly List<TusUpload> _items = [];
    private List<FolderEntryDto> _subfolders = [];
    private DiskUsageDto? _disk;
    private string _subpath = string.Empty;
    private string _newFolder = string.Empty;
    private bool _creatingFolder;
    private bool _dragging;
    private string? _error;
    private int? _successCount;

    [Parameter, EditorRequired] public string LibraryId { get; set; } = string.Empty;

    [Parameter, EditorRequired] public string LibraryPath { get; set; } = string.Empty;

    [Parameter] public string? InitialSubpath { get; set; }

    [Parameter] public EventCallback Uploaded { get; set; }

    [Inject] private FilesystemApi Filesystem { get; set; } = null!;

    [Inject] private HttpClient Http { get; set; } = null!;

    [Inject] private GatewayOptions Gateway { get; set; } = null!;

    private bool HasUploading => _items.Exists(item => item.Status == TusUploadStatus.Uploading);

    private bool HasStartable => _items.Exists(item => item.Status is TusUploadStatus.Idle or TusUploadStatus.Paused or TusUploadStatus.Error);

    private double DiskUsedPercent => _disk is { TotalBytes: > 0 } disk
        ? (disk.TotalBytes - disk.FreeBytes) * (double)PercentScale / disk.TotalBytes
        : 0;

    private bool DiskTight => _disk?.FreeBytes < TightDiskBytes;

    protected override async Task OnInitializedAsync()
    {
        _subpath = InitialSubpath ?? string.Empty;
        await LoadDestinationAsync();
    }

    public void Dispose()
    {
        foreach (var item in _items)
        {
            item.Changed -= OnUploadChanged;
            item.Pause();
        }
    }

    private async Task LoadDestinationAsync()
    {
        if (LibraryPath.Length == 0)
        {
            return;
        }

        var listing = TryAsync(() => Filesystem.BrowseAsync(LibraryPath));
        var disk = TryAsync(() => Filesystem.DiskAsync(LibraryPath));
        _subfolders = (await listing)?.Entries ?? [];
        _disk = await disk;
    }

    private static async Task<T?> TryAsync<T>(Func<Task<T?>> call) where T : class
    {
        try
        {
            return await call();
        }
        catch (ApiException)
        {
            return null;
        }
    }

    private async Task CreateFolderAsync()
    {
        _creatingFolder = true;
        try
        {
            var created = await Filesystem.CreateFolderAsync(LibraryPath, _newFolder.Trim());
            _newFolder = string.Empty;
            _subpath = created?.Name ?? _subpath;
            await LoadDestinationAsync();
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _creatingFolder = false;
        }
    }

    private void OnFilesSelected(InputFileChangeEventArgs e)
    {
        _dragging = false;
        _error = null;
        var subpath = string.IsNullOrWhiteSpace(_subpath) ? null : _subpath.Trim();
        foreach (var file in e.GetMultipleFiles(MaxFilesPerDrop))
        {
            var upload = new TusUpload(Http, Gateway, file, LibraryId, subpath);
            upload.Changed += OnUploadChanged;
            _items.Add(upload);
            _ = StartAsync(upload);
        }
    }

    private async Task StartAsync(TusUpload upload)
    {
        await upload.StartAsync();
        if (upload.Status == TusUploadStatus.Completed)
        {
            _successCount = (_successCount ?? 0) + 1;
            await Uploaded.InvokeAsync();
        }
    }

    private void OnUploadChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task RemoveAsync(TusUpload upload)
    {
        upload.Changed -= OnUploadChanged;
        await upload.CancelAsync();
        _items.Remove(upload);
    }

    private void StartAll()
    {
        foreach (var item in _items.Where(item => item.Status is TusUploadStatus.Idle or TusUploadStatus.Paused or TusUploadStatus.Error))
        {
            _ = StartAsync(item);
        }
    }

    private void PauseAll()
    {
        foreach (var item in _items)
        {
            item.Pause();
        }
    }

    private async Task ClearAllAsync()
    {
        foreach (var item in _items.ToList())
        {
            await RemoveAsync(item);
        }
    }

    private void DragEnter() => _dragging = true;

    private void DragLeave() => _dragging = false;

    private void ClearError() => _error = null;

    private void ClearSuccess() => _successCount = null;
}
