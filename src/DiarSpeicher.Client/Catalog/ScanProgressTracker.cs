using System.Text.Json;
using DiarSpeicher.Client.Api;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Filesystem;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace DiarSpeicher.Client.Catalog;

public sealed class ScanProgressTracker(LibrariesApi libraries, HttpClient http) : IDisposable
{
    private const string DataPrefix = "data:";
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? _stream;
    private string _libraryId = string.Empty;

    public event Action? Changed;

    public DiarSpeicherScanStatusDto? Current { get; private set; }

    public bool Live => Current is { Finished: false };

    public async Task TrackAsync(string libraryId)
    {
        StopStream();
        _libraryId = libraryId;
        Current = null;

        if (libraryId.Length == 0)
        {
            Changed?.Invoke();
            return;
        }

        try
        {
            Current = await libraries.ScanStatusAsync(libraryId);
        }
        catch (ApiException)
        {
            Current = null;
        }

        Changed?.Invoke();
        if (Live)
        {
            _stream = new CancellationTokenSource();
            _ = FollowAsync(libraryId, _stream.Token);
        }
    }

    public void Dispose() => StopStream();

    public static string ScanLabel(DiarSpeicherScanStatusDto progress)
    {
        if (progress.Queued)
        {
            return "EN COLA";
        }

        return progress.Phase switch
        {
            nameof(ScanPhase.ProcessingMedia) => progress.CurrentMedia ?? progress.CurrentSeries ?? "LEYENDO TOMOS",
            nameof(ScanPhase.ProcessingSeries) => progress.CurrentSeries ?? "LEYENDO TOMOS",
            _ when !string.IsNullOrEmpty(progress.Message) => progress.Message,
            nameof(ScanPhase.Started) => "PREPARANDO",
            nameof(ScanPhase.WalkingLibrary) => "RECORRIENDO CARPETAS",
            nameof(ScanPhase.Completed) => "COMPLETADO",
            nameof(ScanPhase.Failed) => "ERROR EN EL ESCANEO",
            _ => "INDEXANDO",
        };
    }

    private async Task FollowAsync(string libraryId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && Live && _libraryId == libraryId)
            {
                await ReadOnceAsync(libraryId, cancellationToken);
                if (Live)
                {
                    await Task.Delay(ReconnectDelay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReadOnceAsync(string libraryId, CancellationToken cancellationToken)
    {
        try
        {
            await ReadStreamAsync(libraryId, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
        }
    }

    private async Task ReadStreamAsync(string libraryId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, libraries.ScanStreamUrl(libraryId));
        request.SetBrowserResponseStreamingEnabled(true);
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var parsed = JsonSerializer.Deserialize(line[DataPrefix.Length..].Trim(), CatalogJsonContext.Default.DiarSpeicherScanStatusDto);
            if (parsed is null || _libraryId != libraryId)
            {
                continue;
            }

            Accumulate(parsed);
            if (parsed.Finished)
            {
                return;
            }
        }
    }

    private void Accumulate(DiarSpeicherScanStatusDto parsed)
    {
        parsed.CreatedMedia = MergeById(Current?.CreatedMedia, parsed.CreatedMedia, media => media.Id);
        parsed.CreatedSeries = MergeById(Current?.CreatedSeries, parsed.CreatedSeries, series => series.Id);
        Current = parsed;
        Changed?.Invoke();
    }

    private static IReadOnlyList<T> MergeById<T>(IReadOnlyList<T>? carried, IReadOnlyList<T>? incoming, Func<T, string> id)
    {
        var baseItems = carried ?? [];
        if (incoming is not { Count: > 0 })
        {
            return baseItems;
        }

        var seen = baseItems.Select(id).ToHashSet(StringComparer.Ordinal);
        var added = incoming.Where(item => !seen.Contains(id(item))).ToList();
        return added.Count == 0 ? baseItems : [.. baseItems, .. added];
    }

    private void StopStream()
    {
        _stream?.Cancel();
        _stream?.Dispose();
        _stream = null;
    }
}
