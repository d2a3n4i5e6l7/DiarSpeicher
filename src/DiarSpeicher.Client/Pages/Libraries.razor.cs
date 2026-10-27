using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Catalog;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DiarSpeicher.Client.Pages;

public sealed partial class Libraries : IDisposable
{
    private static readonly TimeSpan ScanPoll = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan NoticeVisible = TimeSpan.FromSeconds(10);
    private static readonly DialogOptions DeleteOptions = new() { MaxWidth = MaxWidth.Small, FullWidth = true };
    private static readonly DialogOptions UploadOptions = new() { MaxWidth = MaxWidth.Medium, FullWidth = true };

    private List<DiarSpeicherLibraryDto> _libraries = [];
    private List<DiarSpeicherScanStatusDto> _activeScans = [];
    private readonly HashSet<string> _requestedScans = new(StringComparer.Ordinal);
    private bool _loading = true;
    private string? _error;
    private string? _notice;
    private string _query = string.Empty;
    private string _sort = "name";
    private CatalogSortDirection _direction = CatalogSortDirection.Asc;
    private bool _formOpen;
    private DiarSpeicherLibraryDto? _editing;
    private DiarSpeicherLibraryDto? _pendingDelete;
    private bool _deleteOpen;
    private bool _deleteFiles;
    private bool _saving;
    private (DiarSpeicherLibraryDto Library, string Subpath)? _uploadTarget;
    private bool _uploadOpen;
    private CancellationTokenSource? _poll;
    private CancellationTokenSource? _noticeTimer;

    [Inject] private LibrariesApi LibrariesApi { get; set; } = null!;

    private List<DiarSpeicherLibraryDto> Visible => CatalogSorting.FilterAndSortLibraries(_libraries, _query, _sort, _direction);

    protected override async Task OnInitializedAsync() => await LoadAsync(silent: false);

    public void Dispose()
    {
        StopPolling();
        _noticeTimer?.Cancel();
        _noticeTimer?.Dispose();
    }

    private async Task LoadAsync(bool silent)
    {
        if (!silent)
        {
            _loading = true;
        }

        try
        {
            var list = LibrariesApi.ListAsync();
            var scans = TryActiveScansAsync();
            await Task.WhenAll(list, scans);
            _libraries = await list ?? [];
            _activeScans = await scans;
            var active = _activeScans.Select(scan => scan.LibraryId).ToHashSet(StringComparer.Ordinal);
            _requestedScans.IntersectWith(active);
            if (!silent)
            {
                _error = null;
            }
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }

        UpdatePolling();
    }

    private async Task<List<DiarSpeicherScanStatusDto>> TryActiveScansAsync()
    {
        try
        {
            return await LibrariesApi.ActiveScansAsync() ?? [];
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private void UpdatePolling()
    {
        var anyScanning = _activeScans.Count > 0 || _requestedScans.Count > 0;
        if (!anyScanning)
        {
            StopPolling();
            return;
        }

        if (_poll is not null)
        {
            return;
        }

        _poll = new CancellationTokenSource();
        _ = PollAsync(_poll.Token);
    }

    private async Task PollAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(ScanPoll);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                await InvokeAsync(async () =>
                {
                    await LoadAsync(silent: true);
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopPolling()
    {
        _poll?.Cancel();
        _poll?.Dispose();
        _poll = null;
    }

    private void ShowNotice(string message)
    {
        _notice = message;
        _noticeTimer?.Cancel();
        _noticeTimer?.Dispose();
        _noticeTimer = new CancellationTokenSource();
        var token = _noticeTimer.Token;
        _ = Task.Delay(NoticeVisible, token).ContinueWith(
            _ => InvokeAsync(() =>
            {
                _notice = null;
                StateHasChanged();
            }),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private DiarSpeicherScanStatusDto? ActiveFor(DiarSpeicherLibraryDto library) =>
        _activeScans.Find(scan => scan.LibraryId == library.Id);

    private void OpenCreate()
    {
        _editing = null;
        _formOpen = true;
    }

    private void OpenEdit(DiarSpeicherLibraryDto library)
    {
        _editing = library;
        _formOpen = true;
    }

    private void OpenDelete(DiarSpeicherLibraryDto library)
    {
        _deleteFiles = false;
        _pendingDelete = library;
        _deleteOpen = true;
    }

    private void CloseDelete()
    {
        _deleteFiles = false;
        _deleteOpen = false;
    }

    private void OpenUpload(DiarSpeicherLibraryDto library, string subpath)
    {
        _uploadTarget = (library, subpath);
        _uploadOpen = true;
    }

    private async Task CloseUploadAsync()
    {
        _uploadOpen = false;
        _uploadTarget = null;
        await LoadAsync(silent: true);
    }

    private async Task OnSavedAsync(string message)
    {
        ShowNotice(message);
        await LoadAsync(silent: true);
    }

    private async Task DeleteAsync()
    {
        if (_pendingDelete is not { } library)
        {
            return;
        }

        _saving = true;
        _error = null;
        try
        {
            await LibrariesApi.DeleteAsync(library.Id, _deleteFiles);
            ShowNotice(_deleteFiles
                ? $"\"{library.Name}\" eliminada. Recuperable en Restaurar durante una hora."
                : $"Biblioteca \"{library.Name}\" eliminada del índice.");
            _deleteFiles = false;
            _deleteOpen = false;
            _pendingDelete = null;
            await LoadAsync(silent: true);
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task ScanAsync(DiarSpeicherLibraryDto library)
    {
        _requestedScans.Add(library.Id);
        _error = null;
        try
        {
            await LibrariesApi.ScanAsync(library.Id);
            ShowNotice($"Escaneo encolado para \"{library.Name}\".");
            await LoadAsync(silent: true);
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
            _requestedScans.Remove(library.Id);
        }
    }

    private void ClearError() => _error = null;

    private void ClearNotice() => _notice = null;
}
