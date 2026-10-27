using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Catalog;
using DiarSpeicher.Client.Interop;
using DiarSpeicher.Core.Domain.Catalog;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DiarSpeicher.Client.Reader;

public sealed record TocItem(string Label, string Href);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<TocItem>))]
internal sealed partial class EpubJsonContext : JsonSerializerContext;

public sealed partial class EpubReader : IDisposable
{
    private const string ViewerId = "epub-viewer";
    private const string RootId = "epub-root";
    private const int FontStep = 10;
    private const int MinFont = 70;
    private const int MaxFont = 180;
    private const int DefaultFont = 100;
    private const int PercentScale = 100;
    private static readonly TimeSpan UiHideDelay = TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan ProgressDebounce = TimeSpan.FromMilliseconds(1200);

    private static readonly IReadOnlyList<EpubTheme> Themes =
    [
        new("ironBlood", "Core", "#050508"),
        new("pureBlack", "OLED", "#000000"),
        new("sepia", "Sepia", "#1C1814"),
    ];

    private bool _loading = true;
    private string _status = "Conectando con el almacén...";
    private string? _error;
    private bool _uiVisible = true;
    private bool _fullscreen;
    private bool _tocOpen;
    private bool _settingsOpen;
    private List<TocItem> _toc = [];
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

    protected override void OnInitialized()
    {
        _progress = (int)Math.Round(Media.ReadPercent ?? 0);
        EpubJs.DownloadProgress += OnDownloadProgress;
        EpubJs.Relocated += OnRelocated;
        EpubJs.TocLoaded += OnToc;
        EpubJs.Clicked += OnBookClicked;
        ReaderBridgeJs.FullscreenChanged += OnFullscreenChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await _root.FocusAsync(preventScroll: true);
        _status = "Descargando archivo...";
        try
        {
            var fileUrl = MediaApi.FileUrl(Media.Id);
            await EpubJs.Open(ViewerId, fileUrl, await TrySignAsync(fileUrl), _theme.Name);
            _loading = false;
            WakeUi();
        }
        catch (JSException exception)
        {
            _error = string.IsNullOrEmpty(exception.Message) ? "Error al abrir el archivo EPUB." : exception.Message;
            _loading = false;
        }

        StateHasChanged();
    }

    public void Dispose()
    {
        EpubJs.DownloadProgress -= OnDownloadProgress;
        EpubJs.Relocated -= OnRelocated;
        EpubJs.TocLoaded -= OnToc;
        EpubJs.Clicked -= OnBookClicked;
        ReaderBridgeJs.FullscreenChanged -= OnFullscreenChanged;
        Cancel(ref _hideUi);
        if (_progressTimer is not null && _pendingProgress is { } pending)
        {
            _ = SendProgressAsync(pending);
        }

        Cancel(ref _progressTimer);
        EpubJs.Destroy();
    }

    private static async Task<string?> TrySignAsync(string fileUrl)
    {
        try
        {
            return await DpopJs.SignRequestProof("GET", fileUrl);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private void OnDownloadProgress(double loaded, double total) => _ = InvokeAsync(() =>
    {
        var percent = total > 0 ? (int)Math.Round(loaded / total * PercentScale) : 0;
        var totalPart = total > 0 ? $" de {Formatting.FormatBytes((long)total)}" : string.Empty;
        _status = $"Descargando libro: {Formatting.FormatBytes((long)loaded)}{totalPart} ({percent}%)";
        StateHasChanged();
    });

    private void OnRelocated(int percentage, bool completed) => _ = InvokeAsync(() =>
    {
        _progress = percentage;
        ScheduleProgress(new DiarSpeicherUpdateProgressInput { Page = percentage, Percentage = percentage, IsCompleted = completed });
        StateHasChanged();
    });

    private void OnToc(string json) => _ = InvokeAsync(() =>
    {
        _toc = JsonSerializer.Deserialize(json, EpubJsonContext.Default.ListTocItem) ?? [];
        StateHasChanged();
    });

    private void OnBookClicked() => _ = InvokeAsync(() =>
    {
        WakeUi();
        StateHasChanged();
    });

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

    private void GoNext()
    {
        EpubJs.Next();
        WakeUi();
    }

    private void GoPrev()
    {
        EpubJs.Prev();
        WakeUi();
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowRight" or " " or "PageDown":
                GoNext();
                break;
            case "ArrowLeft" or "PageUp":
                GoPrev();
                break;
            case "Escape":
                await Exit.InvokeAsync();
                break;
        }
    }

    private void ChangeFontSize(int delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, MinFont, MaxFont);
        EpubJs.FontSize(_fontSize);
        WakeUi();
    }

    private void SelectTheme(EpubTheme theme)
    {
        _theme = theme;
        EpubJs.SelectTheme(theme.Name);
        WakeUi();
    }

    private void NavigateToChapter(string href)
    {
        EpubJs.Display(href);
        _tocOpen = false;
        WakeUi();
    }

    private void ToggleFullscreen() => ReaderBridgeJs.ToggleFullscreen(RootId);

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
