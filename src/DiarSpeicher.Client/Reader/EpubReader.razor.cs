using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Interop;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DiarSpeicher.Client.Reader;

public sealed partial class EpubReader : IDisposable
{
    private const string PageId = "epub-page";
    private const int FontStep = 10;
    private const int MinFont = 70;
    private const int MaxFont = 180;
    private const int DefaultFont = 100;
    private const int PercentScale = 100;
    private const int NearlyDone = 99;
    private static readonly TimeSpan UiHideDelay = TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan ProgressDebounce = TimeSpan.FromMilliseconds(1200);

    private static readonly IReadOnlyList<EpubTheme> Themes =
    [
        new("ironBlood", "Core", "#050508"),
        new("pureBlack", "OLED", "#000000"),
        new("sepia", "Sepia", "#1C1814"),
    ];

    private readonly ChapterPager _pager = new(PageId);
    private bool _loading = true;
    private string? _error;
    private bool _uiVisible = true;
    private bool _fullscreen;
    private bool _tocOpen;
    private bool _settingsOpen;
    private DiarSpeicherEpubManifestDto _manifest = new();
    private int _chapter;
    private MarkupString _chapterHtml;
    private Action? _positionAfterRender;
    private int _fontSize = DefaultFont;
    private EpubTheme _theme = Themes[0];
    private int _progress;
    private DiarSpeicherUpdateProgressInput? _pendingProgress;
    private CancellationTokenSource? _hideUi;
    private CancellationTokenSource? _progressTimer;
    private ElementReference _root;

    [Parameter, EditorRequired] public DiarSpeicherMediaDto Media { get; set; } = null!;

    [Parameter] public EventCallback Exit { get; set; }

    [Inject] private MediaApi MediaApi { get; set; } = null!;

    private List<long> ChapterSizes => _manifest.ChapterSizes;

    protected override void OnInitialized()
    {
        _progress = (int)Math.Round(Media.ReadPercent ?? 0);
        ReaderBridgeJs.FullscreenChanged += OnFullscreenChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await _root.FocusAsync(preventScroll: true);
            await OpenAsync();
            return;
        }

        if (_positionAfterRender is { } position)
        {
            _positionAfterRender = null;
            position();
            ReportProgress();
        }
    }

    public void Dispose()
    {
        ReaderBridgeJs.FullscreenChanged -= OnFullscreenChanged;
        Cancel(ref _hideUi);
        if (_progressTimer is not null && _pendingProgress is { } pending)
        {
            _ = SendProgressAsync(pending);
        }

        Cancel(ref _progressTimer);
    }

    private async Task OpenAsync()
    {
        try
        {
            _manifest = await MediaApi.EpubManifestAsync(Media.Id) ?? new DiarSpeicherEpubManifestDto();
            if (ChapterSizes.Count == 0)
            {
                _error = "El libro no tiene capítulos legibles.";
            }
            else
            {
                var (chapter, within) = Locate(_progress / (double)PercentScale);
                await LoadChapterAsync(chapter, () => _pager.ShowFraction(within));
                WakeUi();
            }
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }

        _loading = false;
        StateHasChanged();
    }

    private async Task LoadChapterAsync(int chapter, Action position)
    {
        try
        {
            var content = await MediaApi.EpubChapterAsync(Media.Id, chapter);
            _chapter = chapter;
            _chapterHtml = new MarkupString(content?.Html ?? string.Empty);
            _positionAfterRender = position;
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
    }

    private (int Chapter, double Within) Locate(double fraction)
    {
        var target = fraction * ChapterSizes.Sum();
        long before = 0;
        for (var chapter = 0; chapter < ChapterSizes.Count - 1; chapter++)
        {
            var size = ChapterSizes[chapter];
            if (before + size > target)
            {
                return (chapter, size > 0 ? (target - before) / size : 0);
            }

            before += size;
        }

        var last = ChapterSizes[^1];
        return (ChapterSizes.Count - 1, last > 0 ? Math.Clamp((target - before) / last, 0, 1) : 0);
    }

    private void ReportProgress()
    {
        var total = ChapterSizes.Sum();
        if (total <= 0)
        {
            return;
        }

        var read = ChapterSizes.Take(_chapter).Sum() + ChapterSizes[_chapter] * _pager.StartFraction;
        var percentage = Math.Clamp((int)Math.Round(read / total * PercentScale), 1, PercentScale);
        var completed = (_chapter == ChapterSizes.Count - 1 && _pager.IsLast) || percentage >= NearlyDone;
        _progress = percentage;
        ScheduleProgress(new DiarSpeicherUpdateProgressInput { Page = percentage, Percentage = percentage, IsCompleted = completed });
    }

    private void OnFullscreenChanged(bool fullscreen) => _ = InvokeAsync(() =>
    {
        _fullscreen = fullscreen;
        StateHasChanged();
    });

    private void ScheduleProgress(DiarSpeicherUpdateProgressInput payload)
    {
        _pendingProgress = payload;
        Cancel(ref _progressTimer);
        _progressTimer = new CancellationTokenSource();
        _ = DelayThen(ProgressDebounce, _progressTimer.Token, () => SendProgressAsync(payload));
    }

    private async Task SendProgressAsync(DiarSpeicherUpdateProgressInput payload)
    {
        try
        {
            await MediaApi.UpdateProgressAsync(Media.Id, payload);
        }
        catch (ApiException)
        {
        }
    }

    private void WakeUi()
    {
        _uiVisible = true;
        Cancel(ref _hideUi);
        _hideUi = new CancellationTokenSource();
        _ = DelayThen(UiHideDelay, _hideUi.Token, () =>
        {
            _uiVisible = false;
            _settingsOpen = false;
            return InvokeAsync(StateHasChanged);
        });
    }

    private async Task GoNext()
    {
        WakeUi();
        _pager.Measure();
        if (!_pager.IsLast)
        {
            _pager.Show(_pager.Page + 1);
            ReportProgress();
        }
        else if (_chapter < ChapterSizes.Count - 1)
        {
            await LoadChapterAsync(_chapter + 1, () => _pager.Show(0));
        }
    }

    private async Task GoPrev()
    {
        WakeUi();
        _pager.Measure();
        if (_pager.Page > 0)
        {
            _pager.Show(_pager.Page - 1);
            ReportProgress();
        }
        else if (_chapter > 0)
        {
            await LoadChapterAsync(_chapter - 1, _pager.ShowLast);
        }
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowRight" or " " or "PageDown":
                await GoNext();
                break;
            case "ArrowLeft" or "PageUp":
                await GoPrev();
                break;
            case "Escape":
                await Exit.InvokeAsync();
                break;
        }
    }

    private void ChangeFontSize(int delta)
    {
        _pager.Measure();
        var fraction = _pager.StartFraction;
        _fontSize = Math.Clamp(_fontSize + delta, MinFont, MaxFont);
        _positionAfterRender = () => _pager.ShowFraction(fraction);
        WakeUi();
    }

    private void SelectTheme(EpubTheme theme)
    {
        _theme = theme;
        WakeUi();
    }

    private async Task NavigateToChapter(DiarSpeicherEpubTocEntry entry)
    {
        _tocOpen = false;
        WakeUi();
        await LoadChapterAsync(entry.Chapter, entry.Anchor is { } anchor ? () => _pager.ShowAnchor(anchor) : () => _pager.Show(0));
    }

    private static Task ToggleFullscreen() => ReaderBridgeJs.ToggleFullscreen();

    private void OpenToc() => _tocOpen = true;

    private void ToggleSettings() => _settingsOpen = !_settingsOpen;

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

    private sealed record EpubTheme(string Name, string Label, string Background);
}
