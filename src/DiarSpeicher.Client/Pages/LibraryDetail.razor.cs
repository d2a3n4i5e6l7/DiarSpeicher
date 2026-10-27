using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Catalog;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Enums;
using Microsoft.AspNetCore.Components;

namespace DiarSpeicher.Client.Pages;

public sealed partial class LibraryDetail : IDisposable
{

    private DiarSpeicherLibraryDto? _library;
    private List<DiarSpeicherSeriesDto> _series = [];
    private List<DiarSpeicherMediaDto> _volumes = [];
    private string? _error;
    private string? _loadedId;
    private int _gridPageSize = 24;
    private bool _scanLive;
    private int _tab;
    private string _query = string.Empty;
    private string _seriesSort = "name";
    private CatalogSortDirection _seriesDirection = CatalogSortDirection.Asc;
    private string _mediaSort = "createdAt";
    private CatalogSortDirection _mediaDirection = CatalogSortDirection.Desc;
    private int _seriesPage = 1;
    private int _mediaPage = 1;
    private DiarSpeicherMissingReportDto? _missing;
    private bool _missingOpen;
    private bool _purging;
    private List<PreviewSeriesDto> _expected = [];
    private CancellationTokenSource? _load;

    [Parameter] public string Id { get; set; } = string.Empty;

    [Inject] private LibrariesApi LibrariesApi { get; set; } = null!;

    [Inject] private SeriesApi SeriesApi { get; set; } = null!;

    [Inject] private MediaApi MediaApi { get; set; } = null!;

    [Inject] private FilesystemApi Filesystem { get; set; } = null!;

    [Inject] private ScanProgressTracker Scan { get; set; } = null!;

    private bool Scanning => Scan.Live;

    private DiarSpeicherScanStatusDto? Progress => Scan.Current;

    private string? CoverRetryKey => Progress is { } progress ? $"{progress.CompletedSeries}-{progress.CompletedMedia}" : null;

    private List<DiarSpeicherSeriesDto> Series => MergeById(_series, Progress?.CreatedSeries, item => item.Id);

    private List<DiarSpeicherMediaDto> Volumes => MergeById(_volumes, Progress?.CreatedMedia, item => item.Id);

    private int MissingCount => (_missing?.Series.Count ?? 0) + (_missing?.OrphanVolumes ?? 0);

    private List<DiarSpeicherSeriesDto> VisibleSeries => CatalogSorting.FilterAndSortSeries(Series, _query, _seriesSort, _seriesDirection);

    private List<DiarSpeicherMediaDto> VisibleVolumes
    {
        get
        {
            var volumes = Volumes;
            if (Scanning && Progress is { } progress)
            {
                var loaded = volumes.Select(volume => volume.Path).ToHashSet(StringComparer.Ordinal);
                volumes = [.. volumes, .. progress.PendingMedia.Where(path => !loaded.Contains(path)).Select(Placeholder)];
            }

            return CatalogSorting.FilterAndSortMedia(volumes, _query, _mediaSort, _mediaDirection);
        }
    }

    private List<PreviewSeriesDto> PendingSeries
    {
        get
        {
            if (!Scanning)
            {
                return [];
            }

            var done = Series.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
            return _expected.FindAll(item => !done.Contains(item.Name));
        }
    }

    protected override void OnInitialized() => Scan.Changed += OnScanChanged;

    protected override void OnAfterRender(bool firstRender)
    {
        var pageSize = CoverGrid.PageSize();
        if (pageSize != _gridPageSize)
        {
            _gridPageSize = pageSize;
            StateHasChanged();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId == Id)
        {
            return;
        }

        _loadedId = Id;
        _library = null;
        _error = null;
        await Task.WhenAll(ReloadAsync(), Scan.TrackAsync(Id));
        await LoadExpectedAsync();
    }

    public void Dispose()
    {
        Scan.Changed -= OnScanChanged;
        Scan.Dispose();
        _load?.Cancel();
        _load?.Dispose();
    }

    private void OnScanChanged() => _ = InvokeAsync(async () =>
    {
        var live = Scan.Live;
        if (_scanLive && !live)
        {
            await ReloadAsync();
        }

        _scanLive = live;
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = new CancellationTokenSource();
        var token = _load.Token;
        try
        {
            var library = await LibrariesApi.GetAsync(Id, token);
            var series = await LoadAllSeriesAsync(token);
            var perSeries = await Task.WhenAll(series.Select(item => LoadVolumesAsync(item.Id, token)));
            (_library, _series, _volumes, _error) = (library, series, [.. perSeries.SelectMany(list => list)], null);
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await LoadMissingAsync();
    }

    private async Task<List<DiarSpeicherSeriesDto>> LoadAllSeriesAsync(CancellationToken token)
    {
        List<DiarSpeicherSeriesDto> series = [];
        var page = 0;
        var totalPages = 1;
        while (page < totalPages)
        {
            var result = await SeriesApi.ListAsync(Id, page, MediaApi.MaxPageSize, token);
            series.AddRange(result?.Data ?? []);
            totalPages = result?.TotalPages ?? 0;
            page++;
        }

        return series;
    }

    private async Task<List<DiarSpeicherMediaDto>> LoadVolumesAsync(string seriesId, CancellationToken token)
    {
        try
        {
            return (await SeriesApi.MediaAsync(seriesId, 0, MediaApi.MaxPageSize, token))?.Data ?? [];
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private async Task LoadMissingAsync()
    {
        try
        {
            _missing = await LibrariesApi.MissingAsync(Id);
        }
        catch (ApiException)
        {
            _missing = null;
        }
    }

    private async Task LoadExpectedAsync()
    {
        if (!Scanning || _library is null)
        {
            _expected = [];
            return;
        }

        try
        {
            var pattern = _library.Config?.LibraryPattern ?? nameof(LibraryPattern.SeriesBased);
            _expected = (await Filesystem.PreviewAsync(_library.Path, pattern))?.Series ?? [];
        }
        catch (ApiException)
        {
            _expected = [];
        }
    }

    private async Task RescanAsync()
    {
        try
        {
            await LibrariesApi.ScanAsync(Id);
            await Scan.TrackAsync(Id);
            await LoadExpectedAsync();
        }
        catch (ApiException)
        {
        }
    }

    private async Task PurgeAsync()
    {
        _purging = true;
        try
        {
            await LibrariesApi.PurgeMissingAsync(Id);
            _missingOpen = false;
            await ReloadAsync();
        }
        catch (ApiException)
        {
        }
        finally
        {
            _purging = false;
        }
    }

    private DiarSpeicherMediaDto Placeholder(string path)
    {
        var fileName = path.Split('/')[^1];
        return new DiarSpeicherMediaDto
        {
            Id = string.Empty,
            Name = fileName,
            Extension = fileName.Split('.')[^1],
            Status = "Pending",
            Path = path,
            SeriesId = Id,
        };
    }

    private static List<T> MergeById<T>(List<T> loaded, IReadOnlyList<T>? created, Func<T, string> id)
    {
        if (created is not { Count: > 0 })
        {
            return loaded;
        }

        var ids = loaded.Select(id).ToHashSet(StringComparer.Ordinal);
        var added = created.Where(item => !ids.Contains(id(item))).ToList();
        return added.Count == 0 ? loaded : [.. loaded, .. added];
    }

    private List<T> Page<T>(List<T> items, int page) => items.Skip((page - 1) * _gridPageSize).Take(_gridPageSize).ToList();

    private int PageCount(int count) => Math.Max(1, (int)Math.Ceiling(count / (double)_gridPageSize));

    private void OpenMissing() => _missingOpen = true;

    private void CloseMissing() => _missingOpen = false;
}
