using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Catalog;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components;

namespace DiarSpeicher.Client.Pages;

public sealed partial class SeriesDetail : IDisposable
{
    private const int VolumePageSize = 100;
    private const int Complete = 100;
    private const int NextTitleLength = 28;

    private DiarSpeicherSeriesDto? _series;
    private List<DiarSpeicherMediaDto> _volumes = [];
    private string? _error;
    private string? _loadedId;
    private bool _scanLive;
    private int _tab;
    private bool _summaryOpen;
    private bool _matchOpen;
    private bool _renameOpen;
    private bool _coverOpen;
    private bool _deleteOpen;
    private CancellationTokenSource? _load;

    [Parameter] public string Id { get; set; } = string.Empty;

    [Inject] private SeriesApi SeriesApi { get; set; } = null!;

    [Inject] private MediaApi MediaApi { get; set; } = null!;

    [Inject] private MetadataApi MetadataApi { get; set; } = null!;

    [Inject] private ScanProgressTracker Scan { get; set; } = null!;

    private DiarSpeicherSeriesMetadataDto? External => _series?.Metadata;

    private List<DiarSpeicherMediaDto> Volumes
    {
        get
        {
            var mine = Scan.Current?.CreatedMedia.Where(media => media.SeriesId == Id).ToList() ?? [];
            if (mine.Count == 0)
            {
                return _volumes;
            }

            var known = _volumes.Select(volume => volume.Id).ToHashSet(StringComparer.Ordinal);
            var added = mine.FindAll(media => !known.Contains(media.Id));
            return added.Count == 0 ? _volumes : [.. _volumes, .. added];
        }
    }

    private DiarSpeicherScanStatusDto? Indexing =>
        Scan.Current is { Finished: false } current && current.CurrentSeriesId == _series?.Id ? current : null;

    private SeriesStats Stats => SeriesStats.From(Volumes);

    private string? Summary
    {
        get
        {
            var external = External?.Summary?.Trim();
            return string.IsNullOrEmpty(external) ? _series?.Description?.Trim() : external;
        }
    }

    private string VolumeFormats => string.Join(", ",
        Volumes.Select(volume => volume.Extension.TrimStart('.').ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal));

    private string CoverUrl
    {
        get
        {
            if (_series is null)
            {
                return string.Empty;
            }

            if (_series.CoverUpdatedAt is null && Volumes.Count > 0)
            {
                return MediaApi.ThumbnailUrl(Volumes[0].Id);
            }

            return SeriesApi.ThumbnailUrl(_series.Id, _series.CoverUpdatedAt ?? Volumes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private string NextVolumeText(DiarSpeicherMediaDto next)
    {
        var title = MediaHelpers.MediaTitle(next);
        return $"CONTINUAR POR {title[..Math.Min(title.Length, NextTitleLength)].ToUpperInvariant()}";
    }

    protected override void OnInitialized() => Scan.Changed += OnScanChanged;

    private List<VolumeSlot> Slots()
    {
        var volumes = Volumes;
        var byKey = new Dictionary<string, DiarSpeicherMediaDto>(StringComparer.Ordinal);
        foreach (var volume in volumes)
        {
            byKey.TryAdd(FileKey(volume.Path), volume);
        }

        var slots = volumes.Select(volume => new VolumeSlot(volume.Path, volume, Active: false)).ToList();
        var indexing = Indexing;
        foreach (var path in indexing?.PendingMedia ?? [])
        {
            if (!byKey.ContainsKey(FileKey(path)))
            {
                var active = indexing!.ReadingFiles.Any(file => FileKey(file) == FileKey(path));
                slots.Add(new VolumeSlot(path, null, active));
            }
        }

        return slots;
    }

    private static string FileKey(string path) => path.Split('/', '\\')[^1];

    private sealed record VolumeSlot(string Path, DiarSpeicherMediaDto? Volume, bool Active)
    {
        public string FileName => FileKey(Path);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId != Id)
        {
            _loadedId = Id;
            _series = null;
            await ReloadAsync();
            await Scan.TrackAsync(_series?.LibraryId ?? string.Empty);
        }
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
            var detail = SeriesApi.GetAsync(Id, token);
            var media = SeriesApi.MediaAsync(Id, 0, VolumePageSize, token);
            await Task.WhenAll(detail, media);
            _series = await detail;
            _volumes = (await media)?.Data ?? [];
            _error = null;
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task UnmatchAsync()
    {
        try
        {
            await MetadataApi.UnmatchAsync(Id);
        }
        catch (ApiException)
        {
        }

        await ReloadAsync();
    }

    private void OpenMatch() => _matchOpen = true;

    private void OpenRename() => _renameOpen = true;

    private void OpenCover() => _coverOpen = true;

    private void OpenDelete() => _deleteOpen = true;

    private void ToggleSummary() => _summaryOpen = !_summaryOpen;

    private static bool HasValue(object? value) => value switch
    {
        null => false,
        string text => text.Length > 0,
        int number => number != 0,
        long number => number != 0,
        _ => true,
    };

    private sealed record SeriesStats(long TotalPages, long TotalSize, int ReadCount, int Percent, DiarSpeicherMediaDto? NextVolume)
    {
        public static SeriesStats From(List<DiarSpeicherMediaDto> volumes)
        {
            var readCount = volumes.Count(volume => volume.IsCompleted);
            var percent = volumes.Count == 0 ? 0 : (int)Math.Round(readCount * (double)Complete / volumes.Count);
            var next = volumes.Find(volume => !volume.IsCompleted) ?? volumes.FirstOrDefault();
            return new(volumes.Sum(volume => (long)volume.Pages), volumes.Sum(volume => volume.Size), readCount, percent, next);
        }
    }
}
