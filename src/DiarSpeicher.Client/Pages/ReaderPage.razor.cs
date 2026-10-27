using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Interop;
using DiarSpeicher.Client.Reader;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DiarSpeicher.Client.Pages;

public sealed partial class ReaderPage : IDisposable
{
    private const int PreloadAhead = 3;
    private const int StripWindow = 5;
    private const int SwipeThreshold = 60;
    private const string EpubExtension = "epub";
    private const string CanvasId = "reader-canvas";
    private const string StripId = "reader-strip";
    private static readonly TimeSpan UiHideDelay = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan ProgressDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan SliderPreviewDebounce = TimeSpan.FromMilliseconds(150);

    private const double FallbackRatio = 2.0 / 3.0;

    private readonly Dictionary<int, double> _ratios = [];
    private DiarSpeicherMediaDto? _media;
    private string? _error;
    private string? _loadedId;
    private int _page = 1;
    private int _sliderValue = 1;
    private int _previewValue = 1;
    private double _lastRatio = FallbackRatio;
    private ReaderSettings _settings = ReaderSettings.Load() ?? ReaderSettings.Default;
    private bool _uiVisible = true;
    private bool _panelOpen;
    private bool _fullscreen;
    private bool _observing;
    private bool _focusPending;
    private (double X, double Y)? _touchStart;
    private CancellationTokenSource? _hideUi;
    private CancellationTokenSource? _progress;
    private CancellationTokenSource? _preview;
    private ElementReference _canvas;

    [Parameter] public string MediaId { get; set; } = string.Empty;

    [Inject] private MediaApi MediaApi { get; set; } = null!;

    [Inject] private SeriesApi SeriesApi { get; set; } = null!;

    [Inject] private LibrariesApi LibrariesApi { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private int TotalPages => _media?.Pages ?? 0;

    private int Step => _settings.Mode == ReaderMode.Double ? 2 : 1;

    private bool Strip => _settings.Mode == ReaderMode.ContinuousVertical;

    private bool IsEpub => _media is not null &&
        string.Equals(_media.Extension.Trim().TrimStart('.'), EpubExtension, StringComparison.OrdinalIgnoreCase);

    private string CanvasOverflow => Strip || _settings.Fit != ImageFit.Height ? "auto" : "hidden";

    protected override void OnInitialized()
    {
        ReaderBridgeJs.PageVisible += OnPageVisible;
        ReaderBridgeJs.FullscreenChanged += OnFullscreenChanged;
        WakeUi();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId == MediaId)
        {
            return;
        }

        _loadedId = MediaId;
        _media = null;
        _error = null;
        try
        {
            var item = await MediaApi.GetAsync(MediaId);
            var start = item?.CurrentPage is > 0 ? Math.Min(item.CurrentPage.Value, item.Pages) : 1;
            (_media, _page, _sliderValue, _previewValue) = (item, start, start, start);
            _focusPending = true;
            await SeedSettingsFromLibraryAsync(item);
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_media is null || IsEpub)
        {
            return;
        }

        if (Strip != _observing)
        {
            _observing = Strip;
            if (Strip)
            {
                ReaderBridgeJs.ObservePages(StripId);
            }
            else
            {
                ReaderBridgeJs.Disconnect();
            }
        }

        if (_focusPending)
        {
            _focusPending = false;
            await _canvas.FocusAsync(preventScroll: true);
        }
    }

    public void Dispose()
    {
        ReaderBridgeJs.PageVisible -= OnPageVisible;
        ReaderBridgeJs.FullscreenChanged -= OnFullscreenChanged;
        ReaderBridgeJs.Disconnect();
        Cancel(ref _hideUi);
        Cancel(ref _preview);
        Cancel(ref _progress);
        _ = PushProgressAsync(_page);
    }

    private async Task SeedSettingsFromLibraryAsync(DiarSpeicherMediaDto? item)
    {
        if (ReaderSettings.Load() is not null || item?.SeriesId is not { } seriesId)
        {
            return;
        }

        try
        {
            var series = await SeriesApi.GetAsync(seriesId);
            var library = series is null ? null : await LibrariesApi.GetAsync(series.LibraryId);
            if (library?.Config is { } config)
            {
                _settings = ReaderSettings.FromLibrary(config.DefaultReadingMode, config.DefaultReadingDir);
            }
        }
        catch (ApiException)
        {
        }
    }

    private async Task PushProgressAsync(int value)
    {
        if (_media is null || TotalPages <= 0)
        {
            return;
        }

        try
        {
            await MediaApi.UpdateProgressAsync(_media.Id, new DiarSpeicherUpdateProgressInput
            {
                Page = value,
                Percentage = Math.Min(1f, value / (float)TotalPages),
                IsCompleted = value >= TotalPages,
            });
        }
        catch (ApiException)
        {
        }
    }

    private void ScheduleProgress()
    {
        Cancel(ref _progress);
        _progress = new CancellationTokenSource();
        var token = _progress.Token;
        var page = _page;
        _ = DelayThen(ProgressDebounce, token, () => PushProgressAsync(page));
    }

    private void WakeUi()
    {
        _uiVisible = true;
        Cancel(ref _hideUi);
        _hideUi = new CancellationTokenSource();
        _ = DelayThen(UiHideDelay, _hideUi.Token, () =>
        {
            _uiVisible = false;
            return InvokeAsync(StateHasChanged);
        });
    }

    private void GoTo(int next)
    {
        if (TotalPages <= 0)
        {
            return;
        }

        _page = Math.Clamp(next, 1, TotalPages);
        _sliderValue = _page;
        _previewValue = _page;
        ScheduleProgress();
        WakeUi();
    }

    private void GoNext() => GoTo(_page + Step);

    private void GoPrev() => GoTo(_page - Step);

    private void Exit() => Navigation.NavigateTo(_media is null ? string.Empty : $"media/{_media.Id}");

    private void ToggleFullscreen() => ReaderBridgeJs.ToggleFullscreen(CanvasId);

    private void OnKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowRight":
                Turn(forwardOnLtr: true);
                break;
            case "ArrowLeft":
                Turn(forwardOnLtr: false);
                break;
            case " " or "PageDown":
                GoNext();
                break;
            case "PageUp":
                GoPrev();
                break;
            case "f" or "F":
                ToggleFullscreen();
                break;
            case "Escape" when !_fullscreen:
                Exit();
                break;
        }
    }

    private void Turn(bool forwardOnLtr)
    {
        if (forwardOnLtr != _settings.RightToLeft)
        {
            GoNext();
        }
        else
        {
            GoPrev();
        }
    }

    private void OnTouchStart(TouchEventArgs e)
    {
        if (e.Touches.Length > 0)
        {
            _touchStart = (e.Touches[0].ClientX, e.Touches[0].ClientY);
        }
    }

    private void OnTouchEnd(TouchEventArgs e)
    {
        var start = _touchStart;
        _touchStart = null;
        if (start is null || Strip || e.ChangedTouches.Length == 0)
        {
            WakeUi();
            return;
        }

        var dx = e.ChangedTouches[0].ClientX - start.Value.X;
        var dy = e.ChangedTouches[0].ClientY - start.Value.Y;
        if (Math.Abs(dx) < SwipeThreshold || Math.Abs(dx) < Math.Abs(dy))
        {
            WakeUi();
            return;
        }

        Turn(forwardOnLtr: dx < 0);
    }

    private void RememberRatio(int pageNumber)
    {
        var ratio = ReaderBridgeJs.ImageRatio(PageImageId(pageNumber));
        if (ratio > 0)
        {
            _ratios[pageNumber] = ratio;
            _lastRatio = ratio;
        }
    }

    private double RatioOf(int pageNumber) => _ratios.TryGetValue(pageNumber, out var ratio) ? ratio : _lastRatio;

    private static string PageImageId(int pageNumber) => $"reader-page-{pageNumber}";

    private void OnPageVisible(int page) => _ = InvokeAsync(() =>
    {
        _page = page;
        _sliderValue = page;
        _previewValue = page;
        ScheduleProgress();
        StateHasChanged();
    });

    private void OnFullscreenChanged(bool fullscreen) => _ = InvokeAsync(() =>
    {
        _fullscreen = fullscreen;
        StateHasChanged();
    });

    private void UpdateSettings(ReaderSettings next)
    {
        _settings = next;
        _settings.Save();
        WakeUi();
    }

    private void OpenPanel() => _panelOpen = true;

    private int MirrorPage(int value) => _settings.RightToLeft ? Math.Max(1, TotalPages) + 1 - value : value;

    private void OnSliderInput(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var raw))
        {
            return;
        }

        _sliderValue = MirrorPage(raw);
        Cancel(ref _preview);
        _preview = new CancellationTokenSource();
        var target = _sliderValue;
        _ = DelayThen(SliderPreviewDebounce, _preview.Token, () =>
        {
            _previewValue = target;
            return InvokeAsync(StateHasChanged);
        });
    }

    private void OnSliderCommit(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var raw))
        {
            GoTo(MirrorPage(raw));
        }
    }

    private IEnumerable<int> PreloadPages()
    {
        for (var offset = 1; offset <= PreloadAhead && _page + offset <= TotalPages; offset++)
        {
            yield return _page + offset;
        }
    }

    private static async Task DelayThen(TimeSpan delay, CancellationToken token, Func<Task> action)
    {
        try
        {
            await Task.Delay(delay, token);
            await action();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }
}
